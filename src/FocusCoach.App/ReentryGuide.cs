using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using FocusCoach.Core;
using FocusCoach.Platform.Windows;

namespace FocusCoach.App;

/// <summary>再入場ガイドに渡す内容。Anchor は元の作業ウィンドウの位置(WPF の単位)。null なら画面の中央上寄り。</summary>
public sealed record ReentryGuideModel(string TaskName, string SubtaskName, string? NextStep, string? ResumeNote,
    int Seconds, string Message, bool ShowHint, Rect? Anchor);

/// <summary>
/// 再入場ガイド(S-11b。docs/review/design-0.12A-interrupt-closure.md §4)。割り込みを閉じたあと、元の作業のウィンドウの上に重ね、
/// 決まった秒数(既定40秒)で消えて作業中に戻る。その間に何をするかは本人に任せる(書かない)。
/// - フォーカスを奪わない(WS_EX_NOACTIVATE)。後ろの作業ウィンドウでそのまま作業を始められる。
/// - 急かさない: 秒の数字は小さく、主役はゆっくり満ちていく輪。音・点滅は無し。
/// - [今すぐ戻る] / [+30秒]。画面共有には映さない。
/// </summary>
public sealed class ReentryGuideWindow : Window
{
    private const double Ring = 54, Stroke = 4;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly Path _arc = new() { Stroke = Theme.Res<Brush>("AccentGradient"), StrokeThickness = Stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly TextBlock _remaining = new() { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Action<string, double, int> _onEnd;
    private readonly ReentryGuideModel _model;
    private double _total;
    private int _extends;
    private bool _ended;

    public ReentryGuideWindow(ReentryGuideModel model, Action<string, double, int> onEnd)
    {
        _model = model;
        _onEnd = onEnd;
        _total = model.Seconds;
        Title = Loc.T("re.title");
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Theme.Apply(this);
        Background = Brushes.Transparent;

        var body = new StackPanel { Width = 380 };
        body.Children.Add(new TextBlock { Text = Loc.T("re.heading"), FontSize = 13, Foreground = Theme.Res<Brush>("Accent2") });
        var task = model.SubtaskName.Length > 0 ? $"{model.TaskName}  ─  {model.SubtaskName}" : model.TaskName;
        body.Children.Add(new TextBlock { Text = task, FontSize = 17, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        var cue = model.NextStep is { Length: > 0 } n ? Loc.T("re.next", n) : model.ResumeNote is { Length: > 0 } r ? Loc.T("re.progress", r) : "";
        if (cue.Length > 0)
            body.Children.Add(new TextBlock { Text = cue, FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Res<Brush>("Green"), Margin = new Thickness(0, 8, 0, 0) });

        var msgRow = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var ring = new Grid { Width = Ring, Height = Ring, Margin = new Thickness(12, 0, 0, 0) };
        ring.Children.Add(new Ellipse { Stroke = Theme.Res<Brush>("Line"), StrokeThickness = Stroke, Margin = new Thickness(Stroke / 2) });
        ring.Children.Add(_arc);
        _remaining.FontFamily = Theme.Res<FontFamily>("NumFont");
        _remaining.Foreground = Theme.Res<Brush>("TextMuted");
        ring.Children.Add(_remaining);
        DockPanel.SetDock(ring, Dock.Right);
        msgRow.Children.Add(ring);
        var msg = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (model.Message.Length > 0) msg.Children.Add(new TextBlock { Text = model.Message, FontSize = 14, TextWrapping = TextWrapping.Wrap });
        if (model.ShowHint)
            msg.Children.Add(new TextBlock { Text = Loc.T("re.hint"), Style = Theme.Res<Style>("Hint"), TextWrapping = TextWrapping.Wrap });
        msgRow.Children.Add(msg);
        body.Children.Add(msgRow);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var extend = new Button { Content = Loc.T("re.extend", ReentryPolicy.ExtendSec) };
        System.Windows.Automation.AutomationProperties.SetName(extend, Loc.T("re.extend_name"));
        System.Windows.Automation.AutomationProperties.SetAutomationId(extend, "ExtendButton");
        extend.Click += (_, _) => { _total += ReentryPolicy.ExtendSec; _extends++; Update(); };
        var now = new Button { Content = Loc.T("re.now"), Style = Theme.Res<Style>("AccentButton"), Margin = new Thickness(0, 0, 0, 6) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(now, "ReentryNowButton");
        now.Click += (_, _) => End("now");
        buttons.Children.Add(extend);
        buttons.Children.Add(now);
        body.Children.Add(buttons);

        Content = new Border
        {
            Child = body, Padding = new Thickness(18, 14, 18, 10), CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x14, 0x25, 0x2D)),
            BorderBrush = Theme.Res<Brush>("Accent"), BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.6, Color = Colors.Black },
            Margin = new Thickness(12),   // 影の分
        };

        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            NativeMethodsPublic.ExcludeFromCapture(h);
            NativeMethodsPublic.SetExStyle(h, NativeMethodsPublic.GetExStyle(h) | NativeMethodsPublic.NoActivate | NativeMethodsPublic.ToolWindow);
        };
        Loaded += (_, _) => { Place(); Update(); };
        _timer.Tick += (_, _) => Update();
        _timer.Start();
    }

    /// <summary>元の作業ウィンドウの中央・上寄りに置く。無ければ主モニタの作業領域の中央・上寄り。画面の外には出さない。</summary>
    private void Place()
    {
        var a = _model.Anchor ?? SystemParameters.WorkArea;
        var left = a.Left + (a.Width - ActualWidth) / 2;
        var top = a.Top + Math.Max(0, a.Height * 0.18 - 20);
        var screen = new ScreenArea(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        (Left, Top) = HudPlacement.ShrinkToTopLeft(left, top, ActualWidth, ActualHeight, screen);
    }

    private double Elapsed => _watch.Elapsed.TotalSeconds;

    private void Update()
    {
        if (_ended) return;
        var remaining = _total - Elapsed;
        if (remaining <= 0) { End("timeout"); return; }
        _remaining.Text = ReentryPolicy.Remaining(remaining);
        _arc.Data = Arc(Math.Clamp(Elapsed / _total, 0, 1));
    }

    /// <summary>上から時計回りに満ちていく輪(fraction 0〜1)。</summary>
    private static Geometry Arc(double fraction)
    {
        var r = (Ring - Stroke) / 2;
        var c = Ring / 2;
        if (fraction >= 0.999) return new EllipseGeometry(new Point(c, c), r, r);
        var angle = fraction * 2 * Math.PI;
        var start = new Point(c, c - r);
        var end = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));
        var fig = new PathFigure { StartPoint = start, IsClosed = false };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        return new PathGeometry([fig]);
    }

    /// <summary>ガイドを終える(how: timeout / now / interrupt / closed)。記録は呼び出し元へ渡し、自分は薄く消える。</summary>
    public void End(string how)
    {
        if (_ended) return;
        _ended = true;
        _timer.Stop();
        var shown = Elapsed;
        _onEnd(how, shown, _extends);
        if (how is "closed" or "interrupt") { Close(); return; }
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(500));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
