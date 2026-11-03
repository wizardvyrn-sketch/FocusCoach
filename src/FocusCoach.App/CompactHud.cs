using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FocusCoach.Core;
using FocusCoach.Platform.Windows;
using FocusCoach.Storage;

namespace FocusCoach.App;

/// <summary>
/// コンパクト表示(03 S-04、prototype/review/design-0.10A-usability.md R4)。画面を必要な要素だけ(状態・手順名・経過時間・縁の色)に縮めた、
/// 半透明の小さなウィンドウ。透過(AllowsTransparency)はウィンドウを作った後には切り替えられないので、メインのウィンドウとは別にしている。
/// - 通常は半透明で、クリックを下のアプリに通す(WS_EX_TRANSPARENT)。マウスを重ねるとさらに薄くなり、下の画面が見える。
/// - Ctrl を押しながらマウスを重ねている間だけ、不透明になってクリックを受ける([⤢] で元に戻す、ドラッグで移動)。
/// - フォーカスを奪わない(WS_EX_NOACTIVATE)。画面共有・録画には映さない。
/// 見た目の判断(クリック透過・不透明度)は Core の HudPolicy(テスト済み)に任せ、ここは Win32 の反映だけを行う。
/// </summary>
public sealed class CompactHud : Window
{
    private readonly AppSettings _settings;
    private readonly bool _hotkeyOk;
    private readonly Action _onRestore;
    private readonly Action _onInterrupt;
    private readonly Button _interrupt;
    private readonly System.Windows.Shapes.Path _interruptIcon;
    private readonly DispatcherTimer _look = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly Border _root = new();
    private readonly Border _band = new() { Width = 4, CornerRadius = new CornerRadius(2, 0, 0, 2) };
    private readonly System.Windows.Shapes.Ellipse _dot = new() { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _name = new() { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _time = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), FontSize = 15 };
    private readonly TextBlock _mark = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), FontSize = 12 };
    private readonly Button _restore;
    private IntPtr _hwnd;
    private bool _interrupted;
    private bool _clickThrough;
    private bool _dragging;
    private double _lastOpacity = -1;

    public CompactHud(AppSettings settings, bool hotkeyOk, Action onRestore, Action onInterrupt)
    {
        _settings = settings;
        _hotkeyOk = hotkeyOk;
        _onRestore = onRestore;
        _onInterrupt = onInterrupt;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = Loc.T("hud.title");
        Theme.Apply(this);

        _restore = new Button
        {
            Content = "",   // 全画面に戻す(Segoe MDL2 Assets)
            FontFamily = new FontFamily("Segoe MDL2 Assets, Segoe Fluent Icons"),
            FontSize = 11, Width = 30, Height = 26, Margin = new Thickness(8, 0, 4, 0), Padding = new Thickness(0),
            Visibility = Visibility.Collapsed,
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_restore, "RestoreButton");
        _restore.Click += (_, _) => _onRestore();

        // 割り込み対応(しおり)。Ctrl で触れる状態のときだけ出す(design-0.12A §1.2)
        _interruptIcon = new System.Windows.Shapes.Path
        {
            Data = Theme.Res<Geometry>("BookmarkIcon"), Width = 12, Height = 12, Stretch = Stretch.Uniform, Stroke = Theme.Res<Brush>("Accent"),
            StrokeThickness = 1.4, Fill = Theme.Res<Brush>("AccentSoft"), StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };
        _interrupt = new Button
        {
            Content = _interruptIcon, Width = 30, Height = 26, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(0),
            Visibility = Visibility.Collapsed,
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_interrupt, "InterruptButton");
        _interrupt.Click += (_, _) => _onInterrupt();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 8, 0) };
        row.Children.Add(_dot);
        row.Children.Add(_name);
        row.Children.Add(_time);
        row.Children.Add(_mark);
        row.Children.Add(_interrupt);
        row.Children.Add(_restore);
        var dock = new DockPanel { Height = 34 };
        DockPanel.SetDock(_band, Dock.Left);
        dock.Children.Add(_band);
        dock.Children.Add(row);
        _root.Child = dock;
        _root.CornerRadius = new CornerRadius(6);
        _root.BorderThickness = new Thickness(1);
        _root.BorderBrush = Theme.Res<Brush>("Line");
        _root.Background = Theme.Res<Brush>("Panel");
        // 文字が下の画面に埋もれないよう、薄い影を付ける
        _name.Effect = _time.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.9 };
        Content = _root;

        // Ctrl で触れる状態のときだけ、ドラッグで動かせる
        MouseLeftButtonDown += (_, e) =>
        {
            if (_clickThrough || e.OriginalSource is Button) return;
            _dragging = true;
            try { DragMove(); } catch (InvalidOperationException) { /* ボタンが離されている */ }
            _dragging = false;
        };
        SourceInitialized += (_, _) => OnInit();
        _look.Tick += (_, _) => ApplyLook();
        IsVisibleChanged += (_, _) => { if (IsVisible) { _look.Start(); ApplyLook(); } else _look.Stop(); };
        ShowIdle();
    }

    /// <summary>画面の文言に入れるホットキーの名前。</summary>
    public HotkeyNames Keys { get; set; } = HotkeyNames.Default;

    /// <summary>言語・ホットキーの名前が替わったとき、ツールチップと読み上げの名前を作り直す。</summary>
    public void Relabel()
    {
        Title = Loc.T("hud.title");
        _restore.ToolTip = Loc.T("hud.restore_tip", Keys.Compact);
        System.Windows.Automation.AutomationProperties.SetName(_restore, Loc.T("hud.restore_name"));
        _interrupt.ToolTip = Loc.T(_interrupted ? "hud.return_tip" : "hud.interrupt_tip", Keys.Interrupt);
        System.Windows.Automation.AutomationProperties.SetName(_interrupt, Loc.T(_interrupted ? "interrupt.return_name" : "interrupt.name"));
        if (_interrupt.Tag is null) ShowIdle();   // 待機中の文字(言語で替わる)
    }

    private void OnInit()
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        NativeMethodsPublic.ExcludeFromCapture(_hwnd);   // 画面共有・録画に映さない(09章 §9.4.6)
        SetStyle(clickThrough: true);
    }

    /// <summary>作業中の表示を更新する。edgeKey は App.xaml の色の名前(Line / Warn / Accent2 / Paused)。</summary>
    public void Update(StateSnapshot s, string edgeKey, string status)
    {
        var icon = s.Edge switch { PanelEdge.Overrun => "●", PanelEdge.InterruptApp => "◆", PanelEdge.Paused => "❚❚", _ => "" };
        var color = edgeKey == "Line" ? Theme.Res<Brush>("Accent") : Theme.Res<Brush>(edgeKey);
        _band.Background = color;
        _dot.Fill = color;
        _name.Text = s.SubtaskName;
        _name.Foreground = Theme.Res<Brush>("Text");
        _time.Text = TimeSpan.FromSeconds(Math.Max(0, s.SubtaskSec)).ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
        _time.FontFamily = Theme.Res<FontFamily>("NumFont");
        _time.Foreground = s.Edge == PanelEdge.Overrun ? Theme.Res<Brush>("Warn") : Theme.Res<Brush>("Text");
        _mark.Text = icon;
        _mark.Foreground = color;
        var interrupted = s.Paused && s.EdgeReason == "interrupt";
        _interruptIcon.Data = Theme.Res<Geometry>(interrupted ? "ReturnIcon" : "BookmarkIcon");
        _interruptIcon.Fill = interrupted ? Brushes.Transparent : Theme.Res<Brush>("AccentSoft");
        _interrupted = interrupted;
        _interrupt.Tag = "running";
        Relabel();
        System.Windows.Automation.AutomationProperties.SetName(this, $"{status} {s.SubtaskName}");
    }

    /// <summary>開始準備のとき(実行中でない)の表示。</summary>
    public void ShowIdle()
    {
        _band.Background = Theme.Res<Brush>("TextDim");
        _dot.Fill = Theme.Res<Brush>("TextDim");
        _name.Text = "FOCUS COACH  " + Loc.T("status.idle");
        _name.Foreground = Theme.Res<Brush>("TextMuted");
        _time.Text = "";
        _mark.Text = "";
        _interrupt.Tag = null;   // 作業中でないときは出さない
    }

    /// <summary>マウスと Ctrl の状態から、透過とクリック透過を決めて反映する(判断は HudPolicy)。</summary>
    private void ApplyLook()
    {
        if (_dragging || _hwnd == IntPtr.Zero) return;
        var look = HudPolicy.Decide(_hotkeyOk, _settings.CompactClickThrough, CursorOverMe(), NativeMethodsPublic.IsCtrlDown(),
            _settings.CompactOpacity, _settings.CompactHoverOpacity);
        // 背景だけでなく文字ごと薄くする(Window.Opacity)。文字は影で読めるようにしてある
        if (Math.Abs(look.Opacity - _lastOpacity) > 0.001) { Opacity = look.Opacity; _lastOpacity = look.Opacity; }
        if (look.ClickThrough != _clickThrough) SetStyle(look.ClickThrough);
        _restore.Visibility = look.ClickThrough ? Visibility.Collapsed : Visibility.Visible;
        _interrupt.Visibility = look.ClickThrough || _interrupt.Tag is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetStyle(bool clickThrough)
    {
        var ex = NativeMethodsPublic.GetExStyle(_hwnd) | NativeMethodsPublic.NoActivate | NativeMethodsPublic.ToolWindow;
        ex = clickThrough ? ex | NativeMethodsPublic.ClickThrough : ex & ~NativeMethodsPublic.WS_EX_TRANSPARENT;
        NativeMethodsPublic.SetExStyle(_hwnd, ex);
        _clickThrough = clickThrough;
    }

    /// <summary>マウスがこのウィンドウの上にあるか。クリック透過中はマウスイベントが来ないので、カーソルの位置を直接見る。</summary>
    private bool CursorOverMe()
    {
        if (NativeMethodsPublic.CursorPosition() is not { } p) return false;
        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget is null) return false;
        var pt = src.CompositionTarget.TransformFromDevice.Transform(new Point(p.X, p.Y));   // 物理ピクセル → WPF の単位
        return pt.X >= Left && pt.X <= Left + ActualWidth && pt.Y >= Top && pt.Y <= Top + ActualHeight;
    }
}
