using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using FocusCoach.Core;
using FocusCoach.Platform.Windows;

namespace FocusCoach.App;

/// <summary>
/// あとでやること(S-12。prototype/review/design-0.12A-interrupt-closure.md §5)の一覧。0.12A は簡単版:
/// 期限の区分ごとに並べ、チェックで完了、× で削除。完了は7日間だけ下に畳んで見せる(取り消しできるように)。
/// 顧客名が入りうるので、画面共有には映さない。1つだけ開く(もう一度開くと前に出す)。
/// </summary>
public sealed class FollowupListWindow : Window
{
    private static FollowupListWindow? _open;
    private readonly CoreHost _host;
    private readonly Action _changed;
    private readonly StackPanel _list = new();

    private FollowupListWindow(CoreHost host, Action changed)
    {
        _host = host;
        _changed = changed;
        Title = Loc.T("fl.title");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 640;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Theme.Apply(this);
        var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 16) };
        panel.Children.Add(new TextBlock { Text = Loc.T("fl.title"), FontSize = 15, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("fl.hint"),
            Style = Theme.Res<Style>("Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8),
        });
        panel.Children.Add(new ScrollViewer { Content = _list, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Chrome.Frame(this, panel);
        SourceInitialized += (_, _) => NativeMethodsPublic.ExcludeFromCapture(new WindowInteropHelper(this).Handle);
        Closed += (_, _) => _open = null;
        Refresh();
    }

    /// <summary>一覧を開く(開いていれば前に出して読み直す)。changed は件数が変わったとき(ミニパネルの表示を更新する)。</summary>
    public static void ShowOrActivate(CoreHost host, Action changed)
    {
        if (_open is { } w) { w.Refresh(); w.Activate(); return; }
        _open = new FollowupListWindow(host, changed);
        _open.Show();
        _open.Activate();
    }

    /// <summary>開いていれば読み直す(割り込みを閉じて項目が増えたとき)。</summary>
    public static void RefreshIfOpen() => _open?.Refresh();

    private void Refresh()
    {
        _list.Children.Clear();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var items = Followups.Sort(_host.ListFollowups(), today);
        var open = items.Where(i => !i.Done).ToList();
        if (open.Count == 0)
            _list.Children.Add(new TextBlock { Text = Loc.T("fl.empty"), Foreground = Theme.Res<Brush>("TextMuted"), Margin = new Thickness(0, 6, 0, 6) });
        foreach (var g in open.GroupBy(i => Followups.Bucket(i.Due, today)))
        {
            _list.Children.Add(new TextBlock
            {
                Text = Followups.BucketHeading(g.Key), FontSize = 12, Margin = new Thickness(0, 10, 0, 4),
                Foreground = g.Key is DueBucket.Overdue or DueBucket.Today ? Theme.Res<Brush>("Warn") : Theme.Res<Brush>("Accent"),
            });
            foreach (var i in g) _list.Children.Add(Row(i, today));
        }
        var done = items.Where(i => i.Done).ToList();
        if (done.Count > 0)
        {
            var sp = new StackPanel();
            foreach (var i in done) sp.Children.Add(Row(i, today));
            _list.Children.Add(new Expander
            {
                Header = Loc.T("fl.done_header", done.Count), Content = sp, Margin = new Thickness(0, 12, 0, 0),
                Foreground = Theme.Res<Brush>("TextDim"), FontSize = 12,
            });
        }
    }

    private UIElement Row(FollowupItem i, DateOnly today)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 6) };
        var del = new Button { Content = "×", ToolTip = Loc.T("common.delete"), Padding = new Thickness(7, 0, 7, 1), FontSize = 12, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        System.Windows.Automation.AutomationProperties.SetName(del, Loc.T("common.delete"));
        System.Windows.Automation.AutomationProperties.SetAutomationId(del, "DeleteFollowupButton");
        del.Click += (_, _) => Change(i.Id, "deleted");
        DockPanel.SetDock(del, Dock.Right);
        row.Children.Add(del);

        var detail = string.Join(" · ", new[] { Followups.DueLabel(i.Due, today), InterruptCategories.Display(i.Category), i.Title, i.TaskName.Length > 0 ? Loc.T("fl.task", i.TaskName) : "", Loc.DateTime(i.CreatedAt) }
            .Where(x => x.Length > 0));
        var text = new StackPanel { MaxWidth = 340 };
        text.Children.Add(new TextBlock
        {
            Text = i.Text, TextWrapping = TextWrapping.Wrap, FontSize = 13,
            Foreground = i.Done ? Theme.Res<Brush>("TextDim") : Theme.Res<Brush>("Text"),
            TextDecorations = i.Done ? TextDecorations.Strikethrough : null,
        });
        text.Children.Add(new TextBlock { Text = detail, FontSize = 11, Foreground = Theme.Res<Brush>("TextDim"), TextWrapping = TextWrapping.Wrap });
        var cb = new CheckBox { IsChecked = i.Done, Content = text, Foreground = Theme.Res<Brush>("Text"), VerticalContentAlignment = VerticalAlignment.Top };
        System.Windows.Automation.AutomationProperties.SetName(cb, i.Text);
        cb.Click += (_, _) => Change(i.Id, cb.IsChecked == true ? "done" : "open");
        row.Children.Add(cb);
        return row;
    }

    private void Change(long id, string status)
    {
        try { _host.SetFollowupStatus(id, status); }
        catch (Exception ex)
        {
            _host.Log.Error("UI", "followup update failed", ex);
            FcDialog.Info(this, Loc.T("fl.error", ex.Message));
        }
        Refresh();
        _changed();
    }
}
