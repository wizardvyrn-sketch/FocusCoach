using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using FocusCoach.Core;
using FocusCoach.Platform.Windows;

namespace FocusCoach.App;

/// <summary>
/// コードで作るウィンドウ(メモ・手順エディタ・ウィンドウ整理・出口カード・あとでやること・メッセージ)に、
/// メインと同じ細いタイトル帯(「FOCUS COACH」・題名・✕・下の光る線)を付ける。Windows 標準の白いタイトルバーは使わない(03 §3.4.1)。
/// ドラッグ移動は WindowChrome のキャプション(OS の標準動作のまま)。
/// </summary>
internal static class Chrome
{
    public const double CaptionHeight = 30;

    /// <summary>ウィンドウの中身を、タイトル帯付きの枠に入れる。onClose は ✕ を押したとき(既定は Close)。</summary>
    public static void Frame(Window w, UIElement body, Action? onClose = null)
    {
        w.WindowStyle = WindowStyle.None;
        if (w.ResizeMode == ResizeMode.CanResize || w.ResizeMode == ResizeMode.CanResizeWithGrip) w.ResizeMode = ResizeMode.NoResize;
        WindowChrome.SetWindowChrome(w, new WindowChrome
        {
            CaptionHeight = CaptionHeight, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false,
        });

        var bar = new Grid { Height = CaptionHeight, Background = Theme.Res<Brush>("Panel") };
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        left.Children.Add(new TextBlock
        {
            Text = "FOCUS COACH", FontFamily = Theme.Res<FontFamily>("NumFont"), FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Res<Brush>("Accent"), VerticalAlignment = VerticalAlignment.Center,
        });
        var title = new TextBlock { FontSize = 12, Foreground = Theme.Res<Brush>("TextMuted"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Window.Title)) { Source = w });
        left.Children.Add(title);
        bar.Children.Add(left);
        var close = new Button
        {
            Content = "", Style = Theme.Res<Style>("CloseCaptionButton"), Height = CaptionHeight, Width = 42,
            HorizontalAlignment = HorizontalAlignment.Right, ToolTip = Loc.T("common.close"),
        };
        System.Windows.Automation.AutomationProperties.SetName(close, Loc.T("common.close"));
        System.Windows.Automation.AutomationProperties.SetAutomationId(close, "CloseButton");
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        close.Click += (_, _) => (onClose ?? w.Close)();
        bar.Children.Add(close);
        bar.Children.Add(new Border { VerticalAlignment = VerticalAlignment.Bottom, Height = 1, Background = Theme.Res<Brush>("AccentGradient"), Opacity = 0.55 });

        var dock = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);
        dock.Children.Add(body);
        w.Content = new Border { BorderBrush = Theme.Res<Brush>("Line"), BorderThickness = new Thickness(1), Background = Theme.Res<Brush>("Bg"), Child = dock };
    }

    /// <summary>枠を付けたウィンドウの中身(タイトル帯を除いた本文)。描画の確認ツールが使う。</summary>
    public static FrameworkElement Body(Window w) =>
        w.Content is Border { Child: DockPanel d } && d.Children.Count > 1 ? (FrameworkElement)d.Children[1] : (FrameworkElement)w.Content;
}

/// <summary>
/// メッセージ・確認のダイアログ(Windows 標準の MessageBox の代わり。見た目をアプリのテーマにそろえる)。
/// ボタンの文言は、何が起きるかが分かる言葉にできる(例: 「終了して閉じる」「中断して閉じる」「閉じない」)。
/// Enter は既定のボタン、Esc は取り消しのボタン。画面共有には映さない(振り返りなど、タスク名が出るため)。
/// </summary>
public sealed class FcDialog : Window
{
    private MessageBoxResult _result;

    private FcDialog(string text, string title, IReadOnlyList<(string Label, MessageBoxResult Result, bool Accent)> buttons,
        MessageBoxResult defaultResult, MessageBoxResult cancelResult)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Theme.Apply(this);
        _result = cancelResult;

        var body = new StackPanel { Margin = new Thickness(20, 16, 20, 14), MinWidth = 320, MaxWidth = 560 };
        var message = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 21, FontSize = 13 };
        body.Children.Add(new ScrollViewer { Content = message, MaxHeight = 480, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        foreach (var (label, result, accent) in buttons)
        {
            var b = new Button
            {
                Content = label, MinWidth = 88, IsDefault = result == defaultResult, IsCancel = result == cancelResult,
                Margin = new Thickness(8, 0, 0, 0),
            };
            if (accent) b.Style = Theme.Res<Style>("AccentButton");
            b.Margin = new Thickness(8, 0, 0, 0);
            System.Windows.Automation.AutomationProperties.SetAutomationId(b, "Dialog_" + result);   // 言語に依存しない名前(確認スクリプト用)
            var r = result;
            b.Click += (_, _) => { _result = r; DialogResult = true; };
            row.Children.Add(b);
        }
        body.Children.Add(row);
        Chrome.Frame(this, body, onClose: () => { _result = cancelResult; DialogResult = false; });
        SourceInitialized += (_, _) => NativeMethodsPublic.ExcludeFromCapture(new WindowInteropHelper(this).Handle);
        Loaded += (_, _) => { Activate(); (row.Children.OfType<Button>().FirstOrDefault(x => x.IsDefault) ?? row.Children.OfType<Button>().First()).Focus(); };
    }

    /// <summary>お知らせ(OK だけ)。</summary>
    public static void Info(Window? owner, string text, string title = "FocusCoach") =>
        Ask(owner, text, title, [(Loc.T("common.ok"), MessageBoxResult.OK, true)], MessageBoxResult.OK, MessageBoxResult.OK);

    /// <summary>はい / いいえ。文言は変えられる。</summary>
    public static bool Confirm(Window? owner, string text, string? yes = null, string? no = null, string title = "FocusCoach") =>
        Ask(owner, text, title, [(no ?? Loc.T("common.no"), MessageBoxResult.No, false), (yes ?? Loc.T("common.yes"), MessageBoxResult.Yes, true)], MessageBoxResult.Yes, MessageBoxResult.No) == MessageBoxResult.Yes;

    /// <summary>任意のボタン(左から並ぶ)。defaultResult は Enter、cancelResult は Esc・✕。</summary>
    public static MessageBoxResult Ask(Window? owner, string text, string title, IReadOnlyList<(string Label, MessageBoxResult Result, bool Accent)> buttons,
        MessageBoxResult defaultResult, MessageBoxResult cancelResult)
    {
        var d = new FcDialog(text, title, buttons, defaultResult, cancelResult);
        // 所有者が見えていれば、その上の中央に出す(トレイ・コンパクト表示で隠れているときは画面の中央)。
        // Owner は設定しない: 閉じる途中(Closing の中)のウィンドウを Owner にすると WPF が例外にするため、位置だけ合わせる
        if (owner is { IsVisible: true } o && double.IsFinite(o.Left) && double.IsFinite(o.Top))
        {
            d.WindowStartupLocation = WindowStartupLocation.Manual;
            d.Left = o.Left; d.Top = o.Top;
            d.Loaded += (_, _) =>
            {
                var screen = new FocusCoach.Core.ScreenArea(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                (d.Left, d.Top) = FocusCoach.Core.HudPlacement.ShrinkToTopLeft(o.Left + (o.ActualWidth - d.ActualWidth) / 2, o.Top + Math.Max(0, (o.ActualHeight - d.ActualHeight) / 3), d.ActualWidth, d.ActualHeight, screen);
            };
        }
        else d.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        d.ShowDialog();
        return d._result;
    }
}
