using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FocusCoach.Core;
using FocusCoach.Platform.Windows;

namespace FocusCoach.App;

/// <summary>出口カードに渡す内容。Position は左上(WPF の単位)。null なら画面の中央。</summary>
public sealed record ExitCardModel(IReadOnlyList<string> Categories, string InitialCategory, double InterruptSec,
    int TidyCount, bool TidyDefault, bool AllowTidy, Point? Position);

/// <summary>
/// 出口カード(S-11a。docs/review/design-0.12A-interrupt-closure.md §3)。割り込みを閉じて、元の作業に戻る前に開く。
/// - 何も書かずに Enter 1回で閉じられる(完了)。書くのは「あとでやることが残る」ときだけ。
/// - 利用者が F9 / [元の作業へ] を押したときだけ開く。入力のため、この間だけアクティブになる(メモ入力と同じ)。
/// - Esc は「まだ対応を続ける」(割り込み対応中のまま)。画面共有には映さない(件名に顧客名が入りうるため)。
/// </summary>
public sealed class ExitCardWindow : Window
{
    private readonly ComboBox _category = new() { IsEditable = true, FontSize = 14, MinWidth = 220 };
    private readonly TextBox _title = new() { FontSize = 13 };
    private readonly RadioButton _done = new() { Content = Loc.T("exit.done"), GroupName = "outcome", IsChecked = true, Margin = new Thickness(0, 0, 18, 0) };
    private readonly RadioButton _follow = new() { Content = Loc.T("exit.follow"), GroupName = "outcome" };
    private readonly Border _followPanel;
    private readonly TextBox _followText = new() { FontSize = 13 };
    private readonly RadioButton _dueNone = new() { Content = Loc.T("exit.due.none"), GroupName = "due", IsChecked = true };
    private readonly RadioButton _dueToday = new() { Content = Loc.T("exit.due.today"), GroupName = "due" };
    private readonly RadioButton _dueTomorrow = new() { Content = Loc.T("exit.due.tomorrow"), GroupName = "due" };
    private readonly RadioButton _dueDate = new() { Content = Loc.T("exit.due.date"), GroupName = "due" };
    private readonly DatePicker _date = new() { Width = 130, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
    private readonly CheckBox _tidy = new();
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Action<ExitCardInput> _onCommit;
    private readonly Action _onCancel;
    private readonly Action<string> _onArchive;
    private readonly IntPtr _previous;
    private readonly ExitCardModel _model;
    private bool _finished;

    public ExitCardWindow(ExitCardModel model, Action<ExitCardInput> onCommit, Action onCancel, Action<string> onArchive)
    {
        _model = model;
        _onCommit = onCommit;
        _onCancel = onCancel;
        _onArchive = onArchive;
        _previous = NativeMethodsPublic.GetForegroundWindow();
        Title = Loc.T("exit.title");
        Topmost = true;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        if (model.Position is { } p) { WindowStartupLocation = WindowStartupLocation.Manual; Left = p.X; Top = p.Y; }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Theme.Apply(this);
        _error.Foreground = Theme.Res<Brush>("Warn");

        var panel = new StackPanel { Margin = new Thickness(16, 12, 16, 14), Width = 400 };

        // 見出し: 「割り込みを閉じる」と対応時間
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var dur = new TextBlock
        {
            Text = Duration(model.InterruptSec),
            FontFamily = Theme.Res<FontFamily>("NumFont"), Foreground = Theme.Res<Brush>("TextMuted"), VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(dur, Dock.Right);
        head.Children.Add(dur);
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Theme.Res<Geometry>("ReturnIcon"), Width = 14, Height = 14, Stretch = Stretch.Uniform, Stroke = Theme.Res<Brush>("Accent"),
            StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        titleRow.Children.Add(new TextBlock { Text = Loc.T("exit.title"), FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Theme.Res<Brush>("Accent") });
        head.Children.Add(titleRow);
        panel.Children.Add(head);

        // カテゴリ(タスクのプルダウンと同じ: 選ぶか、新しい名前を入力。右クリックで一覧から外す)
        panel.Children.Add(Label(Loc.T("exit.category")));
        foreach (var c in model.Categories) _category.Items.Add(CategoryItem(c));
        _category.Text = InterruptCategories.Display(model.InitialCategory);
        System.Windows.Automation.AutomationProperties.SetName(_category, Loc.T("exit.category"));
        panel.Children.Add(_category);
        panel.Children.Add(new TextBlock { Text = Loc.T("exit.category_hint"), Style = Theme.Res<Style>("Hint") });

        panel.Children.Add(Label(Loc.T("exit.subject")));
        System.Windows.Automation.AutomationProperties.SetName(_title, Loc.T("exit.subject_name"));
        panel.Children.Add(_title);

        // 結果: 完了 / あとでやることが残る
        var outcome = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var r in new[] { _done, _follow }) { r.Foreground = Theme.Res<Brush>("Text"); outcome.Children.Add(r); }
        panel.Children.Add(outcome);

        var fp = new StackPanel();
        fp.Children.Add(new TextBlock { Text = Loc.T("exit.followup_label"), Style = Theme.Res<Style>("FieldLabel"), Margin = new Thickness(0, 0, 0, 3) });
        System.Windows.Automation.AutomationProperties.SetName(_followText, Loc.T("exit.followup_name"));
        fp.Children.Add(_followText);
        var due = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        due.Children.Add(new TextBlock { Text = Loc.T("exit.due"), Foreground = Theme.Res<Brush>("TextMuted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        foreach (var r in new[] { _dueNone, _dueToday, _dueTomorrow, _dueDate })
        {
            r.Foreground = Theme.Res<Brush>("Text");
            r.Margin = new Thickness(0, 0, 12, 0);
            r.VerticalAlignment = VerticalAlignment.Center;
            due.Children.Add(r);
        }
        _date.SelectedDate = DateTime.Today.AddDays(2);
        fp.Children.Add(due);
        fp.Children.Add(_date);   // 「日付」を選んだときだけ出す
        fp.Children.Add(new TextBlock { Text = Loc.T("exit.followup_hint"), Style = Theme.Res<Style>("Hint") });
        _followPanel = new Border
        {
            Child = fp, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(6),
            Background = Theme.Res<Brush>("Panel"), BorderBrush = Theme.Res<Brush>("Line"), BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed,
        };
        panel.Children.Add(_followPanel);
        _done.Checked += (_, _) => _followPanel.Visibility = Visibility.Collapsed;
        _follow.Checked += (_, _) => { _followPanel.Visibility = Visibility.Visible; _followText.Focus(); };
        _dueDate.Checked += (_, _) => _date.Visibility = Visibility.Visible;
        _dueDate.Unchecked += (_, _) => _date.Visibility = Visibility.Collapsed;

        // 片付け: 割り込みで開いたウィンドウを最小化する(閉じない)。離席からの分類し直しでは出さない
        if (model.AllowTidy)
        {
            _tidy.Content = model.TidyCount > 0 ? Loc.T("exit.tidy", model.TidyCount) : Loc.T("exit.tidy0");
            _tidy.IsChecked = model.TidyDefault;
            _tidy.Foreground = Theme.Res<Brush>("Text");
            _tidy.Margin = new Thickness(0, 12, 0, 0);
            _tidy.ToolTip = Loc.T("exit.tidy_tip");
            panel.Children.Add(_tidy);
        }
        panel.Children.Add(_error);

        var buttons = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
        var cancel = new Button { Content = Loc.T("exit.cancel"), Style = Theme.Res<Style>("QuietButton") };
        cancel.Click += (_, _) => Cancel();
        var ok = new Button { Content = Loc.T("exit.ok"), Style = Theme.Res<Style>("AccentButton"), Margin = new Thickness(0, 0, 0, 6) };
        System.Windows.Automation.AutomationProperties.SetName(ok, Loc.T("exit.ok_name"));
        System.Windows.Automation.AutomationProperties.SetAutomationId(ok, "ExitOkButton");
        System.Windows.Automation.AutomationProperties.SetAutomationId(cancel, "ExitCancelButton");
        ok.Click += (_, _) => Commit();
        DockPanel.SetDock(ok, Dock.Right);
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Chrome.Frame(this, panel, onClose: Cancel);

        PreviewKeyDown += OnKey;
        SourceInitialized += (_, _) => NativeMethodsPublic.ExcludeFromCapture(new WindowInteropHelper(this).Handle);
        Loaded += (_, _) =>
        {
            KeepOnScreen();
            Activate();
            _title.Focus();
            Keyboard.Focus(_title);
        };
        Closed += (_, _) => { if (!_finished) { _finished = true; _onCancel(); } };   // × で閉じた = まだ対応を続ける
    }

    /// <summary>対応時間(1時間未満は「12分」、以上は h:mm:ss)。</summary>
    private static string Duration(double sec)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, sec));
        return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : Loc.T("time.m", Math.Max(1, (int)Math.Round(ts.TotalMinutes)));
    }

    private static TextBlock Label(string text) => new() { Text = text, Style = Theme.Res<Style>("FieldLabel"), Margin = new Thickness(0, 6, 0, 3) };

    private ComboBoxItem CategoryItem(string name)
    {
        var item = new ComboBoxItem { Content = InterruptCategories.Display(name) };
        var menu = new ContextMenu();
        var archive = new MenuItem { Header = Loc.T("exit.remove") };
        archive.Click += (_, _) =>
        {
            _onArchive(name);
            _category.Items.Remove(item);
        };
        menu.Items.Add(archive);
        item.ContextMenu = menu;
        return item;
    }

    private void KeepOnScreen()
    {
        if (_model.Position is null) return;
        var screen = new ScreenArea(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var (l, t) = HudPlacement.ShrinkToTopLeft(Left, Top, ActualWidth, ActualHeight, screen);
        Left = l; Top = t;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Cancel(); return; }
        if (e.Key != Key.Enter) return;
        if (_category.IsDropDownOpen || e.OriginalSource is System.Windows.Controls.Primitives.DatePickerTextBox) return;   // 一覧・日付の選択中は、その操作を優先する
        if (e.OriginalSource is Button) return;   // ボタンにフォーカスがあるときは、そのボタンの動作にする(「まだ対応を続ける」で Enter が閉じるにならないように)
        e.Handled = true;
        Commit();
    }

    /// <summary>入力を確かめて閉じる。F9 を押したとき(カードの表示中)もここに来る。</summary>
    public void Commit()
    {
        if (_finished) return;
        var follow = _follow.IsChecked == true;
        var text = _followText.Text.Trim();
        if (follow && text.Length == 0) { ShowError(Loc.T("exit.err_followup")); _followText.Focus(); return; }
        var dueChoice = !follow || _dueNone.IsChecked == true ? "none"
            : _dueToday.IsChecked == true ? "today"
            : _dueTomorrow.IsChecked == true ? "tomorrow"
            : _date.SelectedDate is { } d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
        if (dueChoice.Length == 0) { ShowError(Loc.T("exit.err_due")); return; }
        _finished = true;
        var input = new ExitCardInput(InterruptCategories.Canonical(_category.Text), _title.Text.Trim(), follow, text, dueChoice, _model.AllowTidy && _tidy.IsChecked == true);
        Close();
        _onCommit(input);
    }

    private void Cancel()
    {
        if (_finished) return;
        _finished = true;
        Close();
        if (_previous != IntPtr.Zero) NativeMethodsPublic.SetForegroundWindow(_previous);
        _onCancel();
    }

    private void ShowError(string text)
    {
        _error.Text = text;
        _error.Visibility = Visibility.Visible;
    }
}
