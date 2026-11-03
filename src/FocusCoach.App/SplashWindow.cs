using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FocusCoach.App;

/// <summary>
/// 起動時のロゴ画面(03 §3.8)。同じ作者の Secrypt(NewPWManageTool の docs/08 §1.1)と同じく、デザインシステム **SPIRE**
/// (巨大企業の社内端末風)で組む。本体のテーマ(角丸・強い赤を使わない)とは別で、色・書体はこの画面の中だけで定義する。
/// - 地は void、枠は steel、背景に hairline の 16px 方眼。角は丸めない。corp-red のヘッダーバー。光るものは reactor の進捗線 1 本だけ。
/// - 書体: ワードマーク Oxanium Bold、ラベル・数値 Share Tech Mono(どちらも OFL。Fonts/ に同梱)。
/// - 時間: 表示 0.2 秒 → 進捗線 1.2 秒 → READY で 0.3 秒 → 本体を表示し、その上で 0.25 秒かけて消える(約 1.9 秒)。
/// - クリック、Esc / Enter / Space で飛ばせる。文言は英語だけ(端末の表示という演出。表示言語の設定には従わない)。
/// - 作業の内容を表示しないので、画面キャプチャから除外しない。
/// クラウド(Linux)の型チェックで XAML を扱えないため、コードで組む(ほかのダイアログと同じ)。
/// </summary>
public sealed class SplashWindow : Window
{
    private static readonly TimeSpan HoldAfterBoot = TimeSpan.FromMilliseconds(300);

    // SPIRE の色(この画面だけで使う)
    private static readonly Brush Void = Frozen(0x07, 0x09, 0x0B);
    private static readonly Brush Hairline = Frozen(0x26, 0x30, 0x39);
    private static readonly Brush Steel = Frozen(0x5F, 0x6C, 0x79);
    private static readonly Brush Ink = Frozen(0xE6, 0xEC, 0xEF);
    private static readonly Brush Muted = Frozen(0x8D, 0x9A, 0xA5);
    private static readonly Brush CorpRed = Frozen(0xC8, 0x10, 0x2E);
    private static readonly Color ReactorColor = Color.FromRgb(0x3D, 0xFF, 0xB0);
    private static readonly Brush Reactor = Frozen(0x3D, 0xFF, 0xB0);
    // 書体は FocusCoach のアセンブリに埋め込んでいる(component 指定にして、描画の確認ツールなど別の exe から使っても読めるようにする)
    private static readonly FontFamily Display = new(new Uri("pack://application:,,,/FocusCoach;component/"), "./Fonts/#Oxanium");
    private static readonly FontFamily Mono = new(new Uri("pack://application:,,,/FocusCoach;component/"), "./Fonts/#Share Tech Mono");

    private readonly ScaleTransform _progress = new(0, 1);
    private readonly TextBlock _status;
    private readonly Storyboard _boot = new();
    private bool _completed;

    /// <summary>進捗線が伸びきるか、飛ばされたとき。App はここで本体を表示し、FadeOutAndClose でこの画面を重ねたまま消す。</summary>
    public event EventHandler? BootCompleted;

    public SplashWindow()
    {
        Title = "FocusCoach Boot";   // メインと題名を分ける(題名でウィンドウを探す確認スクリプトが取り違えないように)
        Width = 600; Height = 340;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Opacity = 0;
        Topmost = true;
        ShowInTaskbar = false;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        var rows = new Grid();
        rows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        rows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });

        // ヘッダーバー
        var header = new Grid { Background = CorpRed };
        var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0) };
        left.Children.Add(new TextBlock { Text = "FOCUSCOACH", FontFamily = Display, FontWeight = FontWeights.Bold, FontSize = 13, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center });
        left.Children.Add(Label("//  INTERRUPTION RECOVERY UNIT", Ink, new Thickness(12, 0, 0, 0)));
        header.Children.Add(left);
        var clock = Label($"TERMINAL 01 // {DateTime.Now:HH:mm}", Ink, new Thickness(0, 0, 16, 0));
        clock.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Add(clock);
        Grid.SetRow(header, 0);
        rows.Children.Add(header);

        // ワードマーク(字間を広げるため 1 文字ずつ並べる)と進捗線
        var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        center.Children.Add(Centered(Label("DEEP WORK PROTOCOL  //  ARMED", Muted)));
        var mark = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var ch in "FOCUSCOACH")
            mark.Children.Add(new TextBlock { Text = ch.ToString(), Margin = new Thickness(2.5, 0, 2.5, 0), FontFamily = Display, FontWeight = FontWeights.Bold, FontSize = 52, Foreground = Ink });
        center.Children.Add(mark);
        var line = new Grid { Width = 400, Height = 2, Margin = new Thickness(0, 14, 0, 0) };
        line.Children.Add(new Rectangle { Fill = Hairline });
        line.Children.Add(new Rectangle
        {
            Fill = Reactor, RenderTransformOrigin = new Point(0, 0.5), RenderTransform = _progress,
            Effect = new DropShadowEffect { Color = ReactorColor, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.45 },
        });
        center.Children.Add(line);
        var motto = Centered(Label("INTERRUPT  //  RECOVER  //  REFOCUS", Muted, new Thickness(0, 12, 0, 0)));
        motto.FontSize = 14;
        center.Children.Add(motto);
        Grid.SetRow(center, 1);
        rows.Children.Add(center);

        // ステータス行
        var statusRow = new Grid { Margin = new Thickness(16, 0, 16, 0) };
        var lamp = new StackPanel { Orientation = Orientation.Horizontal };
        lamp.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Reactor, VerticalAlignment = VerticalAlignment.Center });
        _status = Label("STATUS // INITIALIZING", Ink, new Thickness(10, 0, 0, 0));
        lamp.Children.Add(_status);
        statusRow.Children.Add(lamp);
        var right = Label("OPERATOR FOCUS // LOCAL ONLY", Muted);
        right.HorizontalAlignment = HorizontalAlignment.Right;
        statusRow.Children.Add(right);
        var statusBar = new Border { BorderBrush = Hairline, BorderThickness = new Thickness(0, 1, 0, 0), Background = Void, Child = statusRow };
        Grid.SetRow(statusBar, 2);
        rows.Children.Add(statusBar);

        var layers = new Grid();
        layers.Children.Add(new Rectangle { Opacity = 0.55, Fill = GridBrush() });
        layers.Children.Add(rows);
        Content = new Border { Background = Void, BorderBrush = Steel, BorderThickness = new Thickness(1), Child = layers };

        // 表示 0.2 秒、進捗線 1.2 秒(0.15 秒後から)。終わったら READY を 0.3 秒見せて完了
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        Storyboard.SetTarget(fadeIn, this);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
        _boot.Children.Add(fadeIn);
        var grow = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1200))
        {
            BeginTime = TimeSpan.FromMilliseconds(150), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(grow, line.Children[1]);
        Storyboard.SetTargetProperty(grow, new PropertyPath("RenderTransform.ScaleX"));
        _boot.Children.Add(grow);
        _boot.Completed += (_, _) =>
        {
            _status.Text = "STATUS // READY";
            var hold = new DispatcherTimer { Interval = HoldAfterBoot };
            hold.Tick += (_, _) => { hold.Stop(); Complete(); };
            hold.Start();
        };
        Loaded += (_, _) => _boot.Begin(this, isControllable: true);
        MouseLeftButtonDown += (_, _) => Skip();
        KeyDown += (_, e) => { if (e.Key is Key.Escape or Key.Enter or Key.Space) Skip(); };
    }

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }

    /// <summary>SPIRE の label: 等幅・大文字・12px。</summary>
    private static TextBlock Label(string text, Brush color, Thickness margin = default) => new()
    {
        Text = text, FontFamily = Mono, FontSize = 12, Foreground = color, VerticalAlignment = VerticalAlignment.Center, Margin = margin,
    };

    private static TextBlock Centered(TextBlock t) { t.HorizontalAlignment = HorizontalAlignment.Center; return t; }

    /// <summary>背景の 16px 方眼。</summary>
    private static Brush GridBrush()
    {
        var geo = new GeometryGroup();
        geo.Children.Add(new RectangleGeometry(new Rect(0, 0, 16, 1)));
        geo.Children.Add(new RectangleGeometry(new Rect(0, 0, 1, 16)));
        var brush = new DrawingBrush(new GeometryDrawing(Hairline, null, geo))
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 16, 16), ViewboxUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    private void Skip()
    {
        _boot.SkipToFill(this);
        _progress.ScaleX = 1;   // 表示の前(Loaded の前)に飛ばされたときも、伸びきった状態にする
        Opacity = 1;
        _status.Text = "STATUS // READY";
        Complete();
    }

    private void Complete()
    {
        if (_completed) return;
        _completed = true;
        BootCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>本体の上で薄くなって閉じる。</summary>
    public void FadeOutAndClose()
    {
        // Stop すると Opacity が初期値 0 に戻るため、今の値から消す
        var from = Opacity;
        _boot.Stop(this);
        var fade = new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(250));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
