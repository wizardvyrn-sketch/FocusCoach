using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FocusCoach.Core;
using FocusCoach.Core.Workspace;
using FocusCoach.Platform.Windows;

namespace FocusCoach.App;

/// <summary>コードで作るウィンドウに、App.xaml のテーマ(背景・文字色・書体)を当てる。</summary>
internal static class Theme
{
    public static T Res<T>(string key) => (T)Application.Current.FindResource(key);

    public static void Apply(Window w)
    {
        w.Background = Res<Brush>("Bg");
        w.Foreground = Res<Brush>("Text");
        w.FontFamily = Res<FontFamily>("UiFont");
        w.FontSize = 13;
        TextOptions.SetTextFormattingMode(w, TextFormattingMode.Display);
    }
}

/// <summary>
/// 1行メモの小さな入力ウィンドウ(03 S-04、R-02)。ミニパネル本体はフォーカスを奪わないので、文字入力だけはここで行う。
/// 開いている間だけアクティブになり、閉じたら元のウィンドウにフォーカスを返す。画面共有には映さない。
/// </summary>
public sealed class MemoWindow : Window
{
    private readonly TextBox _box = new() { MinWidth = 380, FontSize = 14 };
    private readonly TextBox? _next;
    private readonly IntPtr _previous;
    private readonly Action<string, string> _done;
    private bool _finished;

    private MemoWindow(string prompt, string initial, string? nextInitial, Action<string, string> done)
    {
        _previous = NativeMethodsPublic.GetForegroundWindow();
        _done = done;
        Title = Loc.T("memo.title");
        Topmost = true;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Theme.Apply(this);
        var panel = new StackPanel { Margin = new Thickness(16, 12, 16, 14) };
        panel.Children.Add(new TextBlock { Text = prompt, Foreground = Theme.Res<Brush>("Accent"), Margin = new Thickness(0, 0, 0, 6) });
        _box.Text = initial;
        if (nextInitial is null)
        {
            panel.Children.Add(_box);
            panel.Children.Add(new TextBlock { Text = Loc.T("memo.save_hint"), Style = Theme.Res<Style>("Hint") });
        }
        else
        {
            // 再開メモ(I-01): どこまで / 次の一手。Enter で次の行へ、2行目の Enter(または Ctrl+Enter)で保存
            panel.Children.Add(new TextBlock { Text = Loc.T("memo.done_label"), Style = Theme.Res<Style>("FieldLabel"), Margin = new Thickness(0, 2, 0, 3) });
            panel.Children.Add(_box);
            panel.Children.Add(new TextBlock { Text = Loc.T("memo.next_label"), Style = Theme.Res<Style>("FieldLabel") });
            _next = new TextBox { MinWidth = 380, FontSize = 14, Text = nextInitial };
            _next.KeyDown += OnKey;
            panel.Children.Add(_next);
            panel.Children.Add(new TextBlock { Text = Loc.T("memo.resume_hint"), Style = Theme.Res<Style>("Hint") });
        }
        Chrome.Frame(this, panel, onClose: () => Finish(false));
        _box.KeyDown += OnKey;
        SourceInitialized += (_, _) => NativeMethodsPublic.ExcludeFromCapture(new WindowInteropHelper(this).Handle);
        Loaded += (_, _) => { Activate(); _box.Focus(); Keyboard.Focus(_box); _box.SelectAll(); };
        Deactivated += (_, _) => Finish(false); // 他のウィンドウをクリックしたら取り消し
    }

    /// <summary>1行メモ(棚上げ)。</summary>
    public static void Ask(string prompt, Action<string> done) =>
        new MemoWindow(prompt, "", null, (text, _) => done(text)).Show();

    /// <summary>再開メモ(どこまで / 次の一手)。既存の内容を初期値にする。どちらかが書かれていれば保存する。</summary>
    public static void AskResume(string done, string next, Action<string, string> onSave) =>
        new MemoWindow(Loc.T("memo.resume_title"), done, next, onSave).Show();

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Finish(false); return; }
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (_next is not null && ReferenceEquals(sender, _box) && !ctrl) { _next.Focus(); _next.SelectAll(); return; }
        Finish(true);
    }

    private void Finish(bool save)
    {
        if (_finished) return;
        _finished = true;
        var text = _box.Text.Trim();
        var next = _next?.Text.Trim() ?? "";
        Close();
        if (_previous != IntPtr.Zero) NativeMethodsPublic.SetForegroundWindow(_previous);
        if (save && (text.Length > 0 || next.Length > 0)) _done(text, next);
    }
}

/// <summary>
/// ウィンドウ整理ダイアログ(S-06、F-10)。作業中に開いたウィンドウのうち、閉じるものを選ぶ。
/// 成果物らしいもの(Keep)は既定でチェックを外す。閉じる処理は WM_CLOSE のみ(強制終了しない)。
/// </summary>
public sealed class CloseDialog : Window
{
    private readonly List<(CheckBox Box, WindowInfo Window)> _items = [];
    private bool _close;

    private CloseDialog(IReadOnlyList<WsStep> candidates)
    {
        Title = Loc.T("cd.title");
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        Theme.Apply(this);
        var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 14), MaxWidth = 560 };
        panel.Children.Add(new TextBlock { Text = Loc.T("cd.heading"), FontSize = 15, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("cd.hint"),
            Style = Theme.Res<Style>("Hint"), Margin = new Thickness(0, 4, 0, 10),
        });
        foreach (var s in candidates.Where(c => c.Reason != "not a candidate"))
        {
            var label = $"{s.Window.Process}   {s.Window.Title}" + (s.Action == WsAction.Keep ? Loc.T("cd.keep_note") : "");
            var cb = new CheckBox { Content = label, IsChecked = s.Action == WsAction.Close, Margin = new Thickness(0, 3, 0, 3), Foreground = Theme.Res<Brush>("Text") };
            _items.Add((cb, s.Window));
            panel.Children.Add(cb);
        }
        if (_items.Count == 0) panel.Children.Add(new TextBlock { Text = Loc.T("cd.none"), Foreground = Theme.Res<Brush>("TextMuted") });
        var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var keep = new Button { Content = Loc.T("cd.keep") };
        keep.Click += (_, _) => { _close = false; DialogResult = true; };
        var ok = new Button { Content = Loc.T("cd.ok"), IsDefault = true, Style = Theme.Res<Style>("AccentButton") };
        ok.Click += (_, _) => { _close = true; DialogResult = true; };
        buttons.Children.Add(keep);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        Chrome.Frame(this, panel);
        SourceInitialized += (_, _) => NativeMethodsPublic.ExcludeFromCapture(new WindowInteropHelper(this).Handle);
    }

    /// <summary>閉じるウィンドウの一覧を返す(閉じない場合は空)。</summary>
    public static IReadOnlyList<WindowInfo> Ask(Window owner, IReadOnlyList<WsStep> candidates)
    {
        if (candidates.All(c => c.Reason == "not a candidate")) return [];
        var d = new CloseDialog(candidates) { Owner = owner };
        return d.ShowDialog() == true && d._close ? d._items.Where(i => i.Box.IsChecked == true).Select(i => i.Window).ToList() : [];
    }
}

/// <summary>手順エディタの結果。Register は「このタスクの手順として登録する」を選んだか。</summary>
public sealed record StepsEditResult(List<StepItem> Steps, bool Register);

/// <summary>
/// 手順エディタ(03 S-02a、05 F-01)。手順を1行ずつ、名前と目安(分)で編集する。追加・削除・並べ替えができる。
/// Enter で次の行へ(最後の行では行を追加)。検証は StepList.Normalize(Core。テストあり)。
/// タスク名が出るので、画面共有には映さない。
/// </summary>
public sealed class StepsEditor : Window
{
    private readonly StackPanel _rows = new();
    private readonly CheckBox _register;
    private readonly TextBlock _error;
    private StepsEditResult? _result;

    private StepsEditor(string category, IReadOnlyList<StepItem> steps)
    {
        Title = Loc.T("se.title");
        Width = 470;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Theme.Apply(this);

        var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
        panel.Children.Add(new TextBlock { Text = Loc.T("se.title"), FontSize = 15, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock
        {
            Text = category.Length > 0 ? Loc.T("se.task", category) : Loc.T("se.no_task"),
            Style = Theme.Res<Style>("Hint"), Margin = new Thickness(0, 4, 0, 8),
        });

        var head = RowGrid();
        AddCell(head, 1, new TextBlock { Text = Loc.T("se.col_name"), Style = Theme.Res<Style>("FieldLabel"), Margin = new Thickness(0, 0, 0, 3) });
        AddCell(head, 2, new TextBlock { Text = Loc.T("se.col_min"), Style = Theme.Res<Style>("FieldLabel"), Margin = new Thickness(6, 0, 0, 3) });
        panel.Children.Add(head);
        panel.Children.Add(new ScrollViewer { Content = _rows, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var add = new Button { Content = Loc.T("se.add"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        add.Click += (_, _) => FocusName(AddRow("", ""));
        panel.Children.Add(add);
        panel.Children.Add(new TextBlock { Text = Loc.T("se.hint"), Style = Theme.Res<Style>("Hint") });

        _register = new CheckBox
        {
            Content = Loc.T("se.register"),
            IsChecked = category.Length > 0, IsEnabled = category.Length > 0, Margin = new Thickness(0, 12, 0, 0),
        };
        panel.Children.Add(_register);
        _error = new TextBlock { Foreground = Theme.Res<Brush>("Warn"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        panel.Children.Add(_error);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = Loc.T("common.cancel"), IsCancel = true };
        var ok = new Button { Content = Loc.T("se.ok"), Style = Theme.Res<Style>("AccentButton"), Margin = new Thickness(0, 0, 0, 6) };
        ok.Click += (_, _) => Commit();
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        Chrome.Frame(this, panel);

        foreach (var s in steps) AddRow(s.Name, s.TargetMin is { } m ? m.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "");
        if (steps.Count == 0) AddRow("", "");
        SourceInitialized += (_, _) => NativeMethodsPublic.ExcludeFromCapture(new WindowInteropHelper(this).Handle);
        Loaded += (_, _) => FocusName((Grid)_rows.Children[0]);
    }

    /// <summary>手順を編集する。キャンセルなら null。</summary>
    public static StepsEditResult? Ask(Window owner, string category, IReadOnlyList<StepItem> steps)
    {
        var d = new StepsEditor(category, steps) { Owner = owner };
        return d.ShowDialog() == true ? d._result : null;
    }

    private static Grid RowGrid()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        return g;
    }

    private static void AddCell(Grid g, int col, UIElement e) { Grid.SetColumn(e, col); g.Children.Add(e); }

    private sealed record RowParts(TextBlock Number, TextBox Name, TextBox Minutes);

    private Grid AddRow(string name, string minutes)
    {
        var g = RowGrid();
        g.Margin = new Thickness(0, 0, 0, 6);
        var parts = new RowParts(
            new TextBlock { Foreground = Theme.Res<Brush>("Accent"), FontFamily = Theme.Res<FontFamily>("NumFont"), VerticalAlignment = VerticalAlignment.Center },
            new TextBox { Text = name },
            new TextBox { Text = minutes, Margin = new Thickness(6, 0, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Right });
        g.Tag = parts;
        AddCell(g, 0, parts.Number);
        AddCell(g, 1, parts.Name);
        AddCell(g, 2, parts.Minutes);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        tools.Children.Add(SmallButton("↑", Loc.T("se.up"), () => Move(g, -1)));
        tools.Children.Add(SmallButton("↓", Loc.T("se.down"), () => Move(g, +1)));
        tools.Children.Add(SmallButton("×", Loc.T("common.delete"), () => Remove(g)));
        AddCell(g, 3, tools);
        parts.Name.PreviewKeyDown += (_, e) => OnEnter(e, g, toMinutes: true);
        parts.Minutes.PreviewKeyDown += (_, e) => OnEnter(e, g, toMinutes: false);
        _rows.Children.Add(g);
        Renumber();
        return g;
    }

    private static Button SmallButton(string text, string tip, Action click)
    {
        var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(7, 1, 7, 1), Margin = new Thickness(0, 0, 3, 0), FontSize = 12 };
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    private void OnEnter(KeyEventArgs e, Grid row, bool toMinutes)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (toMinutes) { ((RowParts)row.Tag).Minutes.Focus(); return; }
        var i = _rows.Children.IndexOf(row);
        FocusName(i + 1 < _rows.Children.Count ? (Grid)_rows.Children[i + 1] : AddRow("", ""));
    }

    private static void FocusName(Grid row)
    {
        var box = ((RowParts)row.Tag).Name;
        box.Focus();
        box.SelectAll();
    }

    private void Move(Grid row, int delta)
    {
        var i = _rows.Children.IndexOf(row);
        var j = i + delta;
        if (j < 0 || j >= _rows.Children.Count) return;
        _rows.Children.RemoveAt(i);
        _rows.Children.Insert(j, row);
        Renumber();
    }

    private void Remove(Grid row)
    {
        _rows.Children.Remove(row);
        if (_rows.Children.Count == 0) AddRow("", "");
        Renumber();
    }

    private void Renumber()
    {
        for (var i = 0; i < _rows.Children.Count; i++) ((RowParts)((Grid)_rows.Children[i]).Tag).Number.Text = $"{i + 1}";
    }

    private void Commit()
    {
        var rows = _rows.Children.Cast<Grid>().Select(g => (RowParts)g.Tag).Select(p => (p.Name.Text, p.Minutes.Text));
        var (steps, errors) = StepList.Normalize(rows);
        if (errors.Count > 0)
        {
            _error.Text = string.Join("\n", errors);
            _error.Visibility = Visibility.Visible;
            return;
        }
        _result = new StepsEditResult(steps, _register.IsChecked == true);
        DialogResult = true;
    }
}
