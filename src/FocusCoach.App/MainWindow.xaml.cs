using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FocusCoach.Core;
using FocusCoach.Core.Workspace;
using FocusCoach.Platform.Windows;
using FocusCoach.Storage;

namespace FocusCoach.App;

/// <summary>
/// 開始準備(S-02)とミニパネル(S-04)。ボタン・ホットキー・OSの通知は、すべて CoreHost 経由で CoreCommand にする。
/// 【要ローカル実機確認】クラウドでは型チェックのみ(scripts/check-app-codebehind.sh)。
/// </summary>
public partial class MainWindow : Window
{
    private const int HotkeyInterruptId = 1, HotkeyMemoId = 2, HotkeyCompactId = 3, HotkeyLanguageId = 4;

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private DispatcherTimer? _autoHide;
    private IntPtr _hwnd;
    private bool _noActivate;
    private bool _closeConfirmed;
    private bool _closing;
    private AwayPrompt? _pendingAway;
    /// <summary>画面の文言に入れるホットキーの名前(設定のキーから作る)。</summary>
    private HotkeyNames _keys = HotkeyNames.Default;
    private bool _langHotkeyOk;
    private readonly List<string> _hotkeyFailed = [];

    public CoreHost Host { get; } = new();

    public MainWindow()
    {
        // 表示言語を先に決める(auto は OS の言語に合わせる)。書体も言語に合わせる(prototype/review/design-0.13A-i18n.md)
        Loc.SetLanguage(Loc.Resolve(Host.Settings.Language));
        LangFonts.Apply();
        InitializeComponent();
        Loc.Changed += () => Dispatcher.BeginInvoke(ApplyLanguage);
        Host.Trace = s => Dispatcher.BeginInvoke(() => { LogBox.AppendText(s + Environment.NewLine); LogBox.ScrollToEnd(); });
        Host.LoopCreated += loop => loop.SnapshotChanged += s => Dispatcher.BeginInvoke(() => Render(s));
        Host.AwayPromptRequested += p => Dispatcher.BeginInvoke(() => ShowAwayPrompt(p));
        _tick.Tick += (_, _) => Host.Loop?.Tick();
        TaskAppsBox.Text = Host.Settings.DefaultTaskApps;
        DryRunBox.IsChecked = Host.Settings.WorkspaceDryRun;
        // タスクのプルダウン(入力もできる)。文字が変わるたび(一覧から選んだときも)ガイド・最初の一手・手順を読み直す
        CategoryBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((_, _) => OnCategoryChanged()));
        // 一覧(Popup)は別ウィンドウなので、開くたびに画面共有から除外する(タスク名が映らないように。09章 §9.4.6)
        CategoryBox.DropDownOpened += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (CategoryBox.Template.FindName("PART_Popup", CategoryBox) is System.Windows.Controls.Primitives.Popup { Child: { } child }
                && PresentationSource.FromVisual(child) is HwndSource src)
                NativeMethodsPublic.ExcludeFromCapture(src.Handle);
        }, DispatcherPriority.Loaded);
        RefreshCategories();
        ResetStartPanel();   // 既定は空から始める(前回と同じタスクをまた始めることは少ない。0.10A)
        RefreshRecoverable();
        RefreshFollowups();
        ApplyLanguage();
    }

    // ---------------- ウィンドウの初期化とメッセージ ----------------

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

        // 画面共有・録画に映さない(09章 §9.4.6、R-20)
        if (!NativeMethodsPublic.ExcludeFromCapture(_hwnd)) Host.Log.Warn("UI", "SetWindowDisplayAffinity failed");
        // ロック・リモート切断の通知(F-15)。スリープ(WM_POWERBROADCAST)は登録しなくても届く
        if (!NativeMethodsPublic.RegisterSessionNotification(_hwnd)) Host.Log.Warn("UI", "WTSRegisterSessionNotification failed");

        // F9 / F10(09章 §9.4.1)。登録に失敗したら表示する
        _keys = KeyNames();
        var failed = _hotkeyFailed;
        failed.Clear();
        if (!NativeMethodsPublic.RegisterKey(_hwnd, HotkeyInterruptId, Host.Settings.HotkeyInterrupt)) failed.Add(KeyName(Host.Settings.HotkeyInterrupt));
        if (!NativeMethodsPublic.RegisterKey(_hwnd, HotkeyMemoId, Host.Settings.HotkeyMemo)) failed.Add(KeyName(Host.Settings.HotkeyMemo));
        // F8: コンパクト表示の切り替え。登録できないときは、クリック透過にしない(戻せなくなるため。HudPolicy)
        _compactHotkeyOk = NativeMethodsPublic.RegisterKey(_hwnd, HotkeyCompactId, Host.Settings.HotkeyCompact);
        if (!_compactHotkeyOk) failed.Add(KeyName(Host.Settings.HotkeyCompact));
        // 言語の切り替え(既定は Ctrl+Alt+F8。0 で無効)。修飾キーつきなので、他のアプリのキーを奪いにくい
        if (Host.Settings.HotkeyLanguage != 0)
        {
            _langHotkeyOk = NativeMethodsPublic.RegisterKey(_hwnd, HotkeyLanguageId, Host.Settings.HotkeyLanguage, (uint)Host.Settings.HotkeyLanguageModifiers);
            if (!_langHotkeyOk) failed.Add(_keys.Language);
        }
        if (failed.Count > 0)
        {
            HotkeyText.Text = Loc.T("hotkey.failed", string.Join(", ", failed));
            HotkeyText.Visibility = Visibility.Visible;
            Host.Log.Warn("UI", "hotkey registration failed: " + string.Join(",", failed));
        }
        RestorePosition();
        CreateTray();
    }

    private static string KeyName(int vk) => vk is >= 0x70 and <= 0x87 ? $"F{vk - 0x6F}" : $"0x{vk:X}";

    /// <summary>設定のキーから、画面の文言に入れる名前を作る(設定でキーを変えても文言が嘘にならない)。</summary>
    private HotkeyNames KeyNames()
    {
        var s = Host.Settings;
        var mods = (s.HotkeyLanguageModifiers & 2) != 0 ? "Ctrl+" : "";
        if ((s.HotkeyLanguageModifiers & 1) != 0) mods += "Alt+";
        if ((s.HotkeyLanguageModifiers & 4) != 0) mods += "Shift+";
        return new HotkeyNames(KeyName(s.HotkeyInterrupt), KeyName(s.HotkeyMemo), KeyName(s.HotkeyCompact),
            s.HotkeyLanguage == 0 ? "" : mods + KeyName(s.HotkeyLanguage));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethodsPublic.WM_MOUSEACTIVATE when _noActivate:
                // クリックしてもアクティブにならない(作業中のウィンドウのフォーカスを奪わない。R-45)
                handled = true;
                return new IntPtr(NativeMethodsPublic.MA_NOACTIVATE);
            case NativeMethodsPublic.WM_HOTKEY:
                handled = true;
                if (wParam.ToInt32() == HotkeyInterruptId) _ = ToggleInterruptAsync();
                else if (wParam.ToInt32() == HotkeyMemoId) OpenMemo();
                else if (wParam.ToInt32() == HotkeyCompactId) ToggleCompact();
                else if (wParam.ToInt32() == HotkeyLanguageId) ToggleLanguage();
                break;
            case WindowsSessionEvents.WM_WTSSESSION_CHANGE:
            case WindowsSessionEvents.WM_POWERBROADCAST:
                Host.OnWindowMessage(msg, wParam.ToInt64());
                break;
        }
        return IntPtr.Zero;
    }

    /// <summary>実行中だけフォーカスを奪わない。開始準備では文字入力が要るので通常のウィンドウにする。</summary>
    private void SetNoActivate(bool on)
    {
        _noActivate = on;
        if (_hwnd == IntPtr.Zero) return;
        var ex = NativeMethodsPublic.GetExStyle(_hwnd);
        NativeMethodsPublic.SetExStyle(_hwnd, on ? ex | NativeMethodsPublic.NoActivate : ex & ~NativeMethodsPublic.NoActivate);
    }

    /// <summary>前回の位置に出す。画面外なら主モニタの右上に戻す(R-25)。</summary>
    private void RestorePosition()
    {
        var s = Host.Settings;
        var vx = SystemParameters.VirtualScreenLeft; var vy = SystemParameters.VirtualScreenTop;
        var vw = SystemParameters.VirtualScreenWidth; var vh = SystemParameters.VirtualScreenHeight;
        if (s.PanelLeft is { } l && s.PanelTop is { } t && l >= vx && t >= vy && l + 100 <= vx + vw && t + 50 <= vy + vh)
        {
            Left = l; Top = t;
        }
        else
        {
            Left = SystemParameters.WorkArea.Right - Width - 16;
            Top = SystemParameters.WorkArea.Top + 16;
        }
    }

    // ---------------- 表示言語の切り替え(prototype/review/design-0.13A-i18n.md) ----------------

    /// <summary>タイトル帯のボタン・トレイのメニュー・ホットキー(既定 Ctrl+Alt+F8)から。表示だけを替え、作業の状態・記録には影響しない。</summary>
    private void SetLanguage(string code)
    {
        if (code == Loc.Language) return;
        var from = Loc.Language;
        Loc.SetLanguage(code);   // → Loc.Changed → ApplyLanguage(UI スレッドで)
        Host.Settings.Language = Loc.Language;   // 切り替えたら auto ではなくその言語を保存する(次の起動でも同じ)
        try { Host.Settings.Save(Host.DataRoot); } catch (Exception ex) { Host.Log.Error("UI", "settings save failed", ex); }
        Host.Log.Info("UI", $"language changed {from}->{Loc.Language}");
    }

    private void ToggleLanguage() => SetLanguage(Loc.Next(Loc.Language));

    private void Lang_Click(object sender, RoutedEventArgs e) => ToggleLanguage();

    /// <summary>言語が替わったとき(と起動時)、コードで設定している文言をすべて作り直す。XAML の {loc:T} は束縛で自動で替わる。</summary>
    private void ApplyLanguage()
    {
        _keys = KeyNames();
        LangFonts.Apply();
        StatusText.Text = Loc.T(_statusKey);
        LangButton.ToolTip = _langHotkeyOk ? Loc.T("lang.tooltip", _keys.Language) : Loc.T("lang.tooltip.nokey");
        CompactButton.ToolTip = Loc.T("main.compact_tip", _keys.Compact);
        if (_hotkeyFailed.Count > 0) HotkeyText.Text = Loc.T("hotkey.failed", string.Join(", ", _hotkeyFailed));
        // 開始準備の部品は、開始準備を出しているときだけ作り直す(作業中は隠れている。再開候補の走査は実行中の記録を「途中で終了」と拾ってしまう。
        // 開始準備に戻るとき EnterStartMode が作り直す)
        if (!Host.IsRunning)
        {
            UpdateGuide();
            RefreshCategories();
            ShowSteps(_stepsSource);
            RefreshRecoverable();
        }
        RefreshFollowups();
        if (_lastSnapshot is { } s) Render(s);
        if (AwayPanel.Visibility == Visibility.Visible && _pendingAway is { } p) AwayText.Text = AwayPromptText(p);
        if (ResumeCardPanel.Visibility == Visibility.Visible) FillResumeCard();
        if (_tray is not null) { _tray.Keys = _keys; _tray.Refresh(); }
        if (_hud is not null) { _hud.Keys = _keys; _hud.Relabel(); RenderHud(); }
    }

    // ---------------- 開始準備 ----------------

    private void UpdateGuide()
    {
        try
        {
            var g = Host.LoadGuide(CategoryBox.Text);
            GuideText.Text = g.SampleCount == 0
                ? Loc.T("guide.none")
                : Loc.T("guide.stats", g.SampleCount, T(g.MedianTotalSec!.Value), T(g.TargetSec!.Value), g.Confidence);
        }
        catch (Exception ex) { GuideText.Text = Loc.T("guide.error", ex.Message); }
    }

    // ---------------- タスクのプルダウンと手順(05 F-01・F-02、03 S-02 / S-02a) ----------------

    /// <summary>プルダウンの1行(名前と、回数・最終日などの補足)。入力欄には Name が入る(TextSearch.TextPath)。</summary>
    public sealed record CategoryItem(string Name, string Detail);

    /// <summary>手順。新しいタスクでは空(0.10A: 例の手順は入れない)。</summary>
    private List<StepItem> _steps = [];
    /// <summary>手順などを読み込んだ(または編集した)タスク名。同じ名前の間は読み直さない(編集した内容を保つ)。</summary>
    private string? _stepsCategory;

    /// <summary>自動で入れた値の控え。欄の中身がこれと同じ(または空)のときだけ、タスクを選び直したときに上書きする。</summary>
    private sealed class AutoFilled { public string Project = "", Apps = "", FirstStep = ""; }
    private readonly AutoFilled _auto = new();

    /// <summary>
    /// 過去のタスク(実行がある・手順を登録した・下書きがある)を、最近使った順に一覧にする。
    /// 入力中の文字は変えない。
    /// </summary>
    private void RefreshCategories()
    {
        var text = CategoryBox.Text;
        CategoryBox.ItemsSource = Host.ListCategories().Select(c => new CategoryItem(c.Name, Detail(c))).ToList();
        CategoryBox.Text = text;

        static string Detail(CategoryInfo c)
        {
            var parts = new List<string>();
            if (c.CompletedRuns > 0) parts.Add(Loc.T("cat.runs", c.CompletedRuns, Loc.Date((c.LastCompleted ?? c.LastUsed)!.Value)));
            else if (c.LastUsed is { } u) parts.Add(Loc.T("cat.last", Loc.Date(u)));
            if (c.HasTemplate) parts.Add(Loc.T("cat.template"));
            else if (c.HasDraft && c.CompletedRuns == 0) parts.Add(Loc.T("cat.draft"));
            return string.Join(" · ", parts);
        }
    }

    /// <summary>開始準備を空に戻す(起動時とタスクを終えたとき。作業で使うアプリと練習モードは設定の値を保つ)。</summary>
    private void ResetStartPanel()
    {
        CategoryBox.Text = "";
        ProjectBox.Text = "";
        FirstStepBox.Text = "";
        TaskAppsBox.Text = Host.Settings.DefaultTaskApps;
        _steps = [];
        _stepsCategory = null;
        _auto.Project = ""; _auto.Apps = Host.Settings.DefaultTaskApps; _auto.FirstStep = "";
        ShowSteps(null);
        UpdateGuide();
        UpdateStartEnabled();
    }

    /// <summary>タスク名が空の間は始められない(名前の無い種別ができないように)。</summary>
    private void UpdateStartEnabled()
    {
        var has = CategoryBox.Text.Trim().Length > 0;
        StartButton.IsEnabled = has;
        StartHint.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        CategoryPlaceholder.Visibility = CategoryBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnCategoryChanged()
    {
        UpdateStartEnabled();
        UpdateGuide();
        ApplyTaskFor(CategoryBox.Text.Trim());
    }

    /// <summary>
    /// タスクを選んだら、手順・プロジェクト・使うアプリ・最初の一手を入れる。
    /// 手順は 登録した手順 → 前回の下書き → 最後に終了した実行 の順。ほかの欄は 下書き → 既定値。
    /// 利用者が書き換えた欄は上書きしない。
    /// </summary>
    private void ApplyTaskFor(string category)
    {
        if (category == _stepsCategory) return;
        _stepsCategory = category;
        if (category.Length == 0) { _steps = []; ShowSteps(null); return; }

        if (Host.LoadSteps(category) is { } r)
        {
            _steps = r.Steps;
            ShowSteps(() => Loc.T(r.Source switch
            {
                StepSource.Registered => "steps.src.registered",
                StepSource.Draft => "steps.src.draft",
                _ => "steps.src.last",
            }));
        }
        else
        {
            _steps = [];
            ShowSteps(() => Loc.T("steps.src.new"));
        }
        var draft = Host.LoadDraft(category);
        FillIfUntouched(ProjectBox, ref _auto.Project, draft?.Project ?? "");
        FillIfUntouched(TaskAppsBox, ref _auto.Apps, draft?.TaskApps is { Length: > 0 } a ? a : Host.Settings.DefaultTaskApps);
        FillIfUntouched(FirstStepBox, ref _auto.FirstStep, draft?.FirstStep is { Length: > 0 } f ? f : Host.LoadLastFirstStep(category) ?? "");
    }

    /// <summary>欄が空か、前回自動で入れた値のままなら、新しい値で上書きする(利用者が書いた内容は残す)。</summary>
    private static void FillIfUntouched(TextBox box, ref string auto, string value)
    {
        if (box.Text.Length > 0 && box.Text != auto) return;
        auto = value;
        box.Text = value;
    }

    /// <summary>手順の出どころの文(言語が替わったとき作り直せるよう、文ではなく作り方を持つ)。</summary>
    private Func<string>? _stepsSource;

    private void ShowSteps(Func<string>? source)
    {
        _stepsSource = source;
        StepsText.Text = StepList.Summary(_steps);
        var text = source?.Invoke() ?? "";
        StepsSourceText.Text = text;
        StepsSourceText.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>開始準備の入力を下書きとして保存する(開始・閉じる・手順の決定のとき。タスク名が空なら何もしない)。</summary>
    private void SaveDraftIfAny()
    {
        var category = CategoryBox.Text.Trim();
        if (category.Length == 0) return;
        Host.SaveDraft(new TaskDraft(category, ProjectBox.Text.Trim(), _steps, TaskAppsBox.Text.Trim(), FirstStepBox.Text.Trim(), DateTimeOffset.Now));
    }

    private void EditSteps_Click(object sender, RoutedEventArgs e)
    {
        var category = CategoryBox.Text.Trim();
        if (StepsEditor.Ask(this, category, _steps) is not { } res) return;
        _steps = res.Steps;
        _stepsCategory = category;
        if (res.Register && category.Length > 0)
        {
            try
            {
                Host.SaveTemplate(category, _steps);
                ShowSteps(() => Loc.T("steps.src.registered"));
            }
            catch (Exception ex)
            {
                Host.Log.Error("UI", "step template save failed", ex);
                var msg = ex.Message;
                ShowSteps(() => Loc.T("steps.src.save_error", msg));
            }
        }
        else ShowSteps(() => Loc.T("steps.src.once"));
        SaveDraftIfAny();
        RefreshCategories();
    }

    /// <summary>プルダウンの行を右クリック →「一覧から外す」。実行の記録・手順・下書きは消さない。</summary>
    private void ArchiveCategory_Click(object sender, RoutedEventArgs e)
    {
        // ContextMenu は Popup の中にあるので、MenuItem の DataContext ではなく、開いた元の行の DataContext を見る
        var item = ((sender as MenuItem)?.Parent as ContextMenu)?.PlacementTarget is FrameworkElement fe ? fe.DataContext as CategoryItem : null;
        if (item is null) return;
        try
        {
            Host.ArchiveCategory(item.Name);
            if (CategoryBox.Text.Trim() == item.Name) CategoryBox.Text = "";
            RefreshCategories();
        }
        catch (Exception ex)
        {
            Host.Log.Error("UI", "archive failed", ex);
            FcDialog.Info(this, Loc.T("cat.archive_error", ex.Message));
        }
    }

    /// <summary>中断中(持ち越し)・異常終了した実行を並べる(S-02 上部。F-15、F-16)。</summary>
    private void RefreshRecoverable()
    {
        RecoverPanel.Children.Clear();
        foreach (var r in Host.FindRecoverable())
        {
            var label = Loc.T(r.Kind == "suspended" ? "rec.suspended" : "rec.crashed");
            var box = new StackPanel();
            box.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Res("Accent2") });
            box.Children.Add(new TextBlock
            {
                Text = $"{r.Category} {r.Project}\n{r.ActiveSubtask}  ·  {Loc.DateTime(r.LastEventAt)}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0),
            });
            var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            buttons.Children.Add(MakeButton(Loc.T("rec.resume"), async () => await ResumeRunAsync(r)));
            buttons.Children.Add(MakeButton(Loc.T("rec.end"), async () => { var review = await Host.EndRecoverableAsync(r); FcDialog.Info(this, review, Loc.T("review.title")); RefreshRecoverable(); UpdateGuide(); }));
            buttons.Children.Add(MakeButton(Loc.T("rec.discard"), async () =>
            {
                if (!FcDialog.Confirm(this, Loc.T("rec.discard_confirm"), yes: Loc.T("rec.discard_yes"), no: Loc.T("rec.discard_no"))) return;
                Host.DiscardRecoverable(r);
                RefreshRecoverable();
                await Task.CompletedTask;
            }));
            box.Children.Add(buttons);
            RecoverPanel.Children.Add(new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 10), Child = box });
        }
    }

    private Button MakeButton(string text, Func<Task> action)
    {
        var b = new Button { Content = text };
        b.Click += async (_, _) => await Guarded(action);
        return b;
    }

    /// <summary>App.xaml の色(ブラシ)を名前で取る。</summary>
    private Brush Res(string key) => (Brush)FindResource(key);

    private async void Start_Click(object sender, RoutedEventArgs e) => await Guarded(async () =>
    {
        Host.Settings.WorkspaceDryRun = DryRunBox.IsChecked == true;
        Host.Settings.DefaultTaskApps = TaskAppsBox.Text;
        Host.Settings.Save(Host.DataRoot);
        SaveDraftIfAny();   // 開始する前に、入力を下書きとして残す
        await Host.StartAsync(CategoryBox.Text.Trim(), ProjectBox.Text, StepList.Format(_steps), TaskAppsBox.Text, FirstStepBox.Text.Trim());
        EnterRunMode();
    });

    private async Task ResumeRunAsync(RecoverableRun r)
    {
        var steps = await Host.ResumeAsync(r);
        EnterRunMode();
        if (steps.Count > 0 && FcDialog.Confirm(this, Loc.T("rec.restore_confirm", steps.Count), yes: Loc.T("rec.restore_yes"), no: Loc.T("rec.restore_no")))
        {
            var n = await Host.RestoreWindowsAsync(steps);
            Host.Trace?.Invoke(Loc.T("trace.restored", n));
        }
    }

    private void EnterRunMode()
    {
        StartPanel.Visibility = Visibility.Collapsed;
        RunPanel.Visibility = Visibility.Visible;
        SetNoActivate(true);
        SetStatus("status.working", "Accent");
        _tray?.SetInterruptLabel(true, false);
        _tick.Start();
        Host.Loop?.Tick();
        RefreshFollowups();
    }

    /// <summary>タイトル帯の状態表示(点の色と短い文言)。</summary>
    private string _statusKey = "status.idle";

    private void SetStatus(string statusKey, string colorKey)
    {
        _statusKey = statusKey;
        StatusText.Text = Loc.T(statusKey);
        StatusDot.Fill = Res(colorKey);
        _tray?.SetStatus(statusKey);
    }

    private void EnterStartMode()
    {
        _exitCard?.Close();          // 閉じる = 「まだ対応を続ける」扱い(タスクはもう閉じている)
        _guide?.End("closed");
        _tray?.SetInterruptLabel(false, false);
        _tick.Stop();
        SetNoActivate(false);
        RunPanel.Visibility = Visibility.Collapsed;
        StartPanel.Visibility = Visibility.Visible;
        AwayPanel.Visibility = Visibility.Collapsed;
        ResumeCardPanel.Visibility = Visibility.Collapsed;
        EdgeBorder.BorderBrush = Res("Line");
        SetStatus("status.idle", "TextDim");
        _lastSnapshot = null;
        RenderHud();
        RefreshRecoverable();
        RefreshCategories();   // 終えたタスクをプルダウンに出す
        ResetStartPanel();     // 次は空から始める(0.10A)
        RefreshFollowups();
    }

    // ---------------- コンパクト表示(F8 / ▭)。03 S-04、design-0.10A-usability.md R4 ----------------

    private enum ViewMode { Full, Compact, Tray }
    private ViewMode _view = ViewMode.Full;
    /// <summary>コンパクト表示の間に、利用者に見せる必要があるもの(問いかけ・復帰カード・確認・振り返り)のため一時的に元の表示に戻している。</summary>
    private bool _tempFull;
    private bool _compactHotkeyOk;
    private CompactHud? _hud;

    private void Compact_Click(object sender, RoutedEventArgs e) => ToggleCompact();

    /// <summary>F8 / ▭: 通常 ⇄ コンパクト。一時的に戻しているときは、元の表示のままにする(戻す操作として扱う)。</summary>
    private void ToggleCompact()
    {
        if (_closing || _closeConfirmed) return;
        if (_view == ViewMode.Compact) EnterFull(); else EnterCompact();   // 通常・トレイのどちらからでもコンパクト表示にできる
    }

    /// <summary>仮想スクリーン(全モニタ)。WPF の単位(DPI 補正後)。</summary>
    private static ScreenArea VirtualScreen() => new(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

    private void EnterCompact()
    {
        if (_view == ViewMode.Compact) return;
        var fromTray = _view == ViewMode.Tray;
        RememberBounds();
        if (_hud is null)
        {
            _hud = new CompactHud(Host.Settings, _compactHotkeyOk, onRestore: EnterFull, onInterrupt: () => _ = ToggleInterruptAsync());
            _hud.Keys = _keys;
            _hud.Relabel();
        }
        // 元のウィンドウの左上を起点に縮む(左上の角は動かない)。画面の外には出さない
        _hud.UpdateLayout();
        var mb = MainBounds();
        var (hl, ht) = HudPlacement.ShrinkToTopLeft(mb.L, mb.T, _hud.ActualWidth > 0 ? _hud.ActualWidth : 180, 36, VirtualScreen());
        _hud.Left = hl; _hud.Top = ht;
        _view = ViewMode.Compact; _tempFull = false;
        RenderHud();
        _hud.Show();
        Hide();
        _tray?.SetCompactLabel(true);
        Host.Log.Info("UI", fromTray ? "view compact (from tray)" : "view compact");
    }

    /// <summary>通常表示に戻す(コンパクト表示・トレイのどちらからでも)。コンパクト表示から戻すときは、その左上に元のウィンドウの左上を合わせる。</summary>
    private void EnterFull()
    {
        if (_view == ViewMode.Full) return;
        var from = _view;
        _view = ViewMode.Full; _tempFull = false;
        _hud?.Hide();
        if (from == ViewMode.Compact && _hud is { IsLoaded: true })
        {
            var mb = MainBounds();
            var (l, t) = HudPlacement.RestoreToTopLeft(_hud.Left, _hud.Top, mb.W, mb.H, VirtualScreen());
            Left = l; Top = t;
        }
        if (from != ViewMode.Compact && !_shownBounds.IsEmpty) { Left = _shownBounds.Left; Top = _shownBounds.Top; }   // トレイから戻すときは、隠す前の場所に
        Show();
        _tray?.SetCompactLabel(false);
        Host.Log.Info("UI", from == ViewMode.Tray ? "view full (from tray)" : "view full");
    }

    // ---------------- タスクトレイ(最小化 –)。03 画面一覧「タスクトレイ」 ----------------

    private TrayIcon? _tray;

    /// <summary>タスクトレイのアイコンを作る(常駐。起動中は常に表示する)。</summary>
    private void CreateTray()
    {
        try
        {
            _tray = new TrayIcon(onOpen: RestoreFromTray, onToggleCompact: ToggleCompact, onExit: () => Dispatcher.BeginInvoke(() => SystemCommands.CloseWindow(this)),
                onInterrupt: () => _ = ToggleInterruptAsync(), onFollowups: OpenFollowups, onLanguage: SetLanguage);
            _tray.Keys = _keys;
            _tray.SetStatus(_statusKey);
            _tray.Refresh();
            Host.Log.Info("UI", "tray icon created");
        }
        catch (Exception ex) { Host.Log.Error("UI", "tray icon failed", ex); }   // トレイが作れなくても使える(最小化はタスクバーに残す)
    }

    /// <summary>最小化(–)・Win+M・タスクバーからの最小化: タスクバーではなくタスクトレイに格納する。</summary>
    private void EnterTray()
    {
        if (_view == ViewMode.Tray) return;
        if (_tray is null) { WindowState = WindowState.Minimized; return; }   // トレイが無いときだけ、ふつうの最小化
        var wasCompact = _view == ViewMode.Compact;
        RememberBounds();
        _beforeTray = wasCompact ? ViewMode.Compact : ViewMode.Full;
        _view = ViewMode.Tray; _tempFull = false;
        _hud?.Hide();
        Hide();
        WindowState = WindowState.Normal;   // Win+M などで最小化されていても、戻したときに通常の大きさで出る
        Host.Log.Info("UI", "view tray");
        // 初めて格納したときだけ、場所を知らせる(Windows 11 は新しいアイコンを「^」の中に入れることがある)
        if (!Host.Settings.TrayHintShown)
        {
            Host.Settings.TrayHintShown = true;
            try { Host.Settings.Save(Host.DataRoot); } catch { /* 保存できなくても、次回もう一度出るだけ */ }
            _tray.ShowHint();
        }
    }

    private ViewMode _beforeTray = ViewMode.Full;

    /// <summary>メインの最後に見えていた位置とサイズ(Hide 中は Left/Top が NaN、ActualHeight が 0 になるため、隠す前に控える)。</summary>
    private Rect _shownBounds = Rect.Empty;

    private void RememberBounds()
    {
        if (IsVisible && double.IsFinite(Left) && double.IsFinite(Top) && ActualWidth > 0 && ActualHeight > 0)
            _shownBounds = new Rect(Left, Top, ActualWidth, ActualHeight);
    }

    private (double L, double T, double W, double H) MainBounds() => _shownBounds.IsEmpty
        ? (double.IsFinite(Left) ? Left : 0, double.IsFinite(Top) ? Top : 0, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : 320)
        : (_shownBounds.Left, _shownBounds.Top, _shownBounds.Width, _shownBounds.Height);

    /// <summary>トレイのアイコンのクリックや、2つ目の起動の合図から、格納前の表示に戻す。</summary>
    public void RestoreFromTray()
    {
        if (_closeConfirmed) return;
        if (_view == ViewMode.Tray)
        {
            if (_beforeTray == ViewMode.Compact) EnterCompact(); else EnterFull();
        }
        else if (_view == ViewMode.Full) { Show(); Activate(); }   // すでに通常表示なら、前面に出すだけ
        // コンパクト表示のままなら、そのまま(下のアプリを邪魔しない)
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        // タスクバーのボタンや Win+M などで最小化されたときも、トレイに格納する
        if (WindowState == WindowState.Minimized && _tray is not null) EnterTray();
    }

    private void SaveCompactAnchor(double right, double top) { /* 位置は元のウィンドウの左上に合わせるので、保存しない(0.11A) */ }

    /// <summary>
    /// 利用者に見せる必要があるもの(離席の問いかけ・復帰カード・閉じる確認・振り返り)を出す前に呼ぶ。
    /// コンパクト表示・トレイのときだけ、一時的に元の表示に戻す(閉じたら EndTemporaryFull で戻る)。
    /// </summary>
    private void ShowFullTemporarily(string why)
    {
        if (_view == ViewMode.Full || _tempFull) return;
        _tempFull = true;
        _hud?.Hide();
        Show();
        Host.Log.Info("UI", $"view full (temporary: {why})");
    }

    /// <summary>ShowFullTemporarily を、現在のイベント処理を抜けてから行う(Closing の内側など、Show できない場面用)。</summary>
    private void ShowFullTemporarilyLater(string why, Action then)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ShowFullTemporarily(why);
            then();
        });
    }

    /// <summary>問いかけ・カード・ダイアログが閉じた(または自動で畳まれた)とき。一時的に戻していたら、コンパクト表示(またはトレイ)に戻す。</summary>
    private void EndTemporaryFull()
    {
        if (!_tempFull || _view == ViewMode.Full) return;
        // まだ見せているものがあれば、そのまま(畳まれたあとで戻る)
        if (AwayPanel.Visibility == Visibility.Visible || ResumeCardPanel.Visibility == Visibility.Visible) return;
        _tempFull = false;
        Hide();
        if (_view == ViewMode.Compact) { RenderHud(); _hud?.Show(); Host.Log.Info("UI", "view compact (back)"); }
        else Host.Log.Info("UI", "view tray (back)");
    }

    private StateSnapshot? _lastSnapshot;
    private string _lastEdgeKey = "Line";

    private void RenderHud()
    {
        if (_hud is null) return;
        if (_lastSnapshot is { } s && Host.IsRunning) _hud.Update(s, _lastEdgeKey, StatusText.Text);
        else _hud.ShowIdle();
    }

    // ---------------- 作業中 ----------------

    private static string T(double sec) => TimeSpan.FromSeconds(Math.Max(0, sec)).ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);

    private void Render(StateSnapshot s)
    {
        _lastSnapshot = s;
        StepText.Text = $"STEP {s.SubtaskNumber} / {s.SubtaskCount}";
        SubtaskText.Text = s.SubtaskName;
        TimerText.Text = T(s.SubtaskSec);
        TargetText.Text = s.SubtaskTargetSec is { } t ? Loc.T("run.target", T(t)) : "";
        SubtaskProgress.Value = s.SubtaskTargetSec is { } t2 && t2 > 0 ? Math.Min(1, s.SubtaskSec / t2) : 0;
        ElapsedText.Text = Loc.T("run.elapsed", T(s.ElapsedSec));
        // 最初の一手(I-05): 最初のサブタスクの間だけ出す
        FirstStepText.Text = s.FirstStep.Length > 0 ? Loc.T("run.first_step", s.FirstStep) : "";
        FirstStepText.Visibility = s.FirstStep.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        // 09章 §9.4.4: 縁の色とアイコンだけで知らせる(ポップアップ・音は出さない)。強い赤・橙は使わない
        var (edge, icon, text) = s.Edge switch
        {
            PanelEdge.Overrun => ("Warn", "●", Loc.T("edge.overrun")),
            PanelEdge.InterruptApp => ("Accent2", "◆", Loc.T("edge.interruptapp", _keys.Interrupt)),
            PanelEdge.Paused when s.EdgeReason == "interrupt" => ("Paused", "❚❚", Loc.T("edge.bookmarked", _keys.Interrupt, _keys.Memo)),
            PanelEdge.Paused => ("Paused", "❚❚", Loc.T("edge.paused", ReasonName(s.EdgeReason), _keys.Interrupt, _keys.Memo)),
            _ => ("Line", "", ""),
        };
        EdgeBorder.BorderBrush = Res(edge);
        EdgeText.Foreground = Res(edge == "Line" ? "TextMuted" : edge);
        EdgeText.Text = text.Length > 0 ? $"{icon}  {text}" : "";
        SubtaskProgress.Foreground = s.Edge == PanelEdge.Overrun ? Res("Warn") : Res("AccentGradient");
        // しおりを挟んだ割り込み対応中は、「中断」ではなく「割り込み対応中」と出す(design-0.12A §1.2)
        var interrupted = s.Paused && s.EdgeReason == "interrupt";
        if (interrupted) SetStatus("status.interrupted", "Paused"); else if (s.Paused) SetStatus("status.paused", "Paused"); else SetStatus("status.working", "Accent");
        InterruptLabel.Text = Loc.T(interrupted ? "interrupt.return_label" : s.Paused ? "interrupt.resume_label" : "interrupt.label", _keys.Interrupt);
        InterruptIcon.Data = (Geometry)FindResource(interrupted ? "ReturnIcon" : "BookmarkIcon");
        InterruptIcon.Fill = interrupted ? Brushes.Transparent : Res("AccentSoft");
        System.Windows.Automation.AutomationProperties.SetName(InterruptButton, Loc.T(interrupted ? "interrupt.return_name" : s.Paused ? "interrupt.resume_name" : "interrupt.name"));
        InterruptButton.ToolTip = Loc.T(interrupted ? "interrupt.return_tip" : "interrupt.tip", _keys.Interrupt);
        _tray?.SetInterruptLabel(true, interrupted);
        // 割り込みアプリに滞在中は、割り込みボタンを強調する(09章 §9.4.4)
        InterruptButton.Style = (Style)FindResource(s.Edge is PanelEdge.InterruptApp or PanelEdge.Paused ? "AccentButton" : typeof(Button));
        MemoButton.Content = Loc.T(s.Paused ? "interrupt.resume_memo" : "interrupt.memo", _keys.Memo);
        _lastEdgeKey = edge;
        if (_view == ViewMode.Compact && !_tempFull) _hud?.Update(s, edge, StatusText.Text);
    }

    private static string ReasonName(string? reason) =>
        Loc.T("reason." + (reason is "interrupt" or "lock" or "sleep" or "remote" or "meeting" or "break" or "idle" ? reason : "other"));

    private void Minimize_Click(object sender, RoutedEventArgs e) => EnterTray();   // タスクバーではなくタスクトレイへ(0.11A)
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private async void Next_Click(object sender, RoutedEventArgs e) => await Guarded(() => Host.SendAsync(CoreCommand.Of(Cmd.NextSubtask)));
    private async void Skip_Click(object sender, RoutedEventArgs e) => await Guarded(() => Host.SendAsync(CoreCommand.Of(Cmd.SkipSubtask)));
    private async void Interrupt_Click(object sender, RoutedEventArgs e) => await ToggleInterruptAsync();
    private void Memo_Click(object sender, RoutedEventArgs e) => OpenMemo();

    /// <summary>
    /// F9 / しおり: ワンクリックで割り込み対応に入る(入力も確認も求めない)。対応中にもう一度押すと出口カード(S-11a)を開き、
    /// カードの表示中の F9 は Enter と同じ(F9 を2回で「完了」で閉じる)。再入場ガイドの表示中に押したら、新しい割り込みとして扱う。
    /// design-0.12A §2。
    /// </summary>
    private async Task ToggleInterruptAsync() => await Guarded(async () =>
    {
        if (!Host.IsRunning) return;
        if (_exitCard is { } card) { card.Commit(); return; }
        _guide?.End("interrupt");
        if (Host.IsInterrupted) { OpenExitCard("f9"); return; }
        var evs = await Host.ToggleInterruptAsync();   // 割り込み対応に入る(ロックなど別の理由の一時停止なら、今までどおり再開)
        if (Host.IsInterrupted) ResumeCardPanel.Visibility = Visibility.Collapsed;
        if (evs.Any(x => x.Type == Ev.ResumeCardShown)) ShowResumeCard();
    });

    // ---------------- 割り込みの出口と再入場(S-11a / S-11b。design-0.12A-interrupt-closure.md) ----------------

    private ExitCardWindow? _exitCard;
    private ReentryGuideWindow? _guide;
    /// <summary>離席を割り込みに分類し直したときの、その長さ(秒)。出口カードと再入場ガイドの判断に使う。</summary>
    private double _awaySec;

    /// <summary>出口カードを開く。via: f9(割り込み対応中から)/ away(離席を割り込みに分類し直した直後。片付けはしない)。</summary>
    private void OpenExitCard(string via)
    {
        if (_exitCard is not null) { _exitCard.Activate(); return; }
        var categories = Host.ListInterruptCategories();
        var model = new ExitCardModel(categories, categories.FirstOrDefault() ?? InterruptCategories.Default,
            via == "f9" ? Host.InterruptElapsedSec : _awaySec, via == "f9" ? Host.PreviewTidyCount() : 0,
            Host.Settings.ExitCardTidyDefault, AllowTidy: via == "f9", ExitCardPosition());
        _exitCard = new ExitCardWindow(model,
            onCommit: input => OnExitCardCommit(input, via),
            onCancel: () => { _exitCard = null; Host.Log.Info("UI", "exit card cancelled"); EndTemporaryFull(); },
            onArchive: name =>
            {
                try { Host.ArchiveInterruptCategory(name); }
                catch (Exception ex) { Host.Log.Error("UI", "interrupt category archive failed", ex); }
            });
        _exitCard.Show();
        Host.Log.Info("UI", $"exit card shown via={via}");
    }

    /// <summary>出口カードの置き場所: ミニパネル・コンパクト表示のすぐ下。トレイに格納中は画面の中央(null)。</summary>
    private Point? ExitCardPosition()
    {
        if (_view == ViewMode.Compact && !_tempFull && _hud is { IsVisible: true } h) return new Point(h.Left, h.Top + h.ActualHeight + 8);
        if (IsVisible && double.IsFinite(Left) && double.IsFinite(Top)) return new Point(Left, Top + ActualHeight + 8);
        return null;
    }

    private void OnExitCardCommit(ExitCardInput input, string via)
    {
        _exitCard = null;
        // 元の作業のウィンドウを前に出す。カードがアクティブな今のうちに行う(後からだと Windows がフォーカスの移動を許さないことがある)
        var work = Host.InterruptWorkWindow(via);
        if (work != IntPtr.Zero && !NativeMethodsPublic.SetForegroundWindow(work)) Host.Log.Warn("UI", "work window could not be activated");
        _ = Guarded(() => CloseInterruptAsync(input, via, work));
    }

    private async Task CloseInterruptAsync(ExitCardInput input, string via, IntPtr work)
    {
        var sec = via == "f9" ? Host.InterruptElapsedSec : _awaySec;
        var d = ReentryPolicy.Decide(sec, Host.Settings.ReentryGuideSec, Host.Settings.ReentryGuideMinInterruptSec);
        var res = await Host.CloseInterruptAsync(input, d.Show ? d.Seconds : 0, via);
        RefreshFollowups();
        if (d.Show) { ShowGuide(d.Seconds, work); EndTemporaryFull(); }
        else if (via == "away" || res.Events.Any(x => x.Type == Ev.ResumeCardShown)) ShowResumeCard();   // ガイドを出さないときは、今までの復帰カード
        else EndTemporaryFull();
    }

    /// <summary>再入場ガイド(S-11b)を、元の作業ウィンドウの上に出す。</summary>
    private void ShowGuide(int sec, IntPtr work)
    {
        var s = Host.Session!.State;
        var c = ResumeCard.Build(s, DateTimeOffset.Now);
        Rect? anchor = null;
        if (NativeMethodsPublic.WindowRect(work) is { } r)
        {
            var dpi = VisualTreeHelper.GetDpi(this);   // 物理ピクセル → WPF の単位
            anchor = new Rect(r.X / dpi.DpiScaleX, r.Y / dpi.DpiScaleY, r.W / dpi.DpiScaleX, r.H / dpi.DpiScaleY);
        }
        var hint = !Host.Settings.ReentryGuideHintShown;
        if (hint)
        {
            Host.Settings.ReentryGuideHintShown = true;
            try { Host.Settings.Save(Host.DataRoot); } catch { /* 保存できなくても、次回もう一度出るだけ */ }
        }
        _guide = new ReentryGuideWindow(
            new ReentryGuideModel(s.Category, c.SubtaskName, c.NextStep, c.ResumeNote, sec, ReentryPolicy.Message(Host.Settings.ReentryGuideMessage, sec), hint, anchor),
            onEnd: (how, shown, extends) => OnGuideEnd(how, shown, extends, work));
        _guide.Show();
        Host.Log.Info("UI", $"reentry guide shown sec={sec} anchored={(anchor is not null ? "yes" : "no")}");
    }

    private void OnGuideEnd(string how, double shown, int extends, IntPtr work)
    {
        _guide = null;
        _ = Guarded(() => Host.EndReentryAsync(how, shown, extends));
        // 時間切れでは、他のアプリで入力中ならフォーカスを移さない([今すぐ戻る] は利用者が選んだので移す)
        var move = how == "now" || (how == "timeout" && ReentryPolicy.MayMoveFocus(NativeMethodsPublic.SecondsSinceLastInput()));
        if (move && work != IntPtr.Zero && NativeMethodsPublic.GetForegroundWindow() != work && !NativeMethodsPublic.SetForegroundWindow(work))
            Host.Log.Info("UI", "work window was not activated after the guide");
    }

    // ---------------- あとでやること(S-12) ----------------

    private void Followups_Click(object sender, RoutedEventArgs e) => OpenFollowups();

    private void OpenFollowups() => FollowupListWindow.ShowOrActivate(Host, RefreshFollowups);

    /// <summary>ミニパネルの「あとでやること n」を更新する(1件以上あるときだけ出す)。</summary>
    private void RefreshFollowups()
    {
        var badge = Followups.Badge(Host.ListFollowups(), DateOnly.FromDateTime(DateTime.Now));
        FollowupText.Text = badge;
        FollowupButton.Visibility = badge.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        FollowupListWindow.RefreshIfOpen();
    }

    /// <summary>
    /// F10 / 📝: 別の小さな入力ウィンドウでメモ(R-02)。閉じたら元のウィンドウにフォーカスを返す。
    /// 割り込み中は「どこまで / 次の一手」の2行(I-01)、作業中は棚上げの1行。
    /// </summary>
    private void OpenMemo()
    {
        if (!Host.IsRunning) return;
        if (Host.Session!.State.IsPaused) OpenResumeNote();
        else MemoWindow.Ask(Loc.T("memo.defer_prompt"), async text => await Guarded(() => Host.DeferAsync(text)));
    }

    private void OpenResumeNote()
    {
        var card = ResumeCard.Build(Host.Session!.State, DateTimeOffset.Now);
        MemoWindow.AskResume(card.ResumeNote ?? "", card.NextStep ?? "", async (done, next) =>
            await Guarded(async () =>
            {
                await Host.ResumeNoteAsync(done, next);
                if (ResumeCardPanel.Visibility == Visibility.Visible) ShowResumeCard(); // カードを開いていれば内容を更新する
            }));
    }

    private void EditNote_Click(object sender, RoutedEventArgs e) => OpenResumeNote();

    private void ShowAwayPrompt(AwayPrompt p)
    {
        ShowFullTemporarily("away_prompt");
        _pendingAway = p;
        AwayText.Text = AwayPromptText(p);
        AwayPanel.Visibility = Visibility.Visible;
        AutoHide(AwayPanel);
    }

    private static string AwayPromptText(AwayPrompt p) =>
        Loc.T("away.prompt", Loc.T("reason." + (p.Kind is "lock" or "sleep" or "remote" ? p.Kind : "idle")), p.Minutes);

    private async void AwayYes_Click(object sender, RoutedEventArgs e) => await Guarded(async () =>
    {
        AwayPanel.Visibility = Visibility.Collapsed;
        if (_pendingAway is null) return;
        _awaySec = _pendingAway.Minutes * 60;
        var evs = await Host.ReclassifyAsync(_pendingAway.Target);
        _pendingAway = null;
        // 割り込みとして記録したら、出口カードでカテゴリ・あとでやることを書けるようにする(design-0.12A §3.3)
        if (evs.Any(x => x.Type == Ev.ResumeCardShown)) OpenExitCard("away"); else EndTemporaryFull();
    });

    private void AwayNo_Click(object sender, RoutedEventArgs e) { AwayPanel.Visibility = Visibility.Collapsed; _pendingAway = null; EndTemporaryFull(); }

    /// <summary>復帰カード(09章 §9.4.2)。次の一手を一番上に出す(I-01)。</summary>
    private void ShowResumeCard()
    {
        ShowFullTemporarily("resume_card");
        FillResumeCard();
        ResumeCardPanel.Visibility = Visibility.Visible;
        AutoHide(ResumeCardPanel);
    }

    /// <summary>復帰カードの文字を作る(言語が替わったときも、開いていれば作り直す)。</summary>
    private void FillResumeCard()
    {
        if (Host.Session is null) return;
        var c = ResumeCard.Build(Host.Session.State, DateTimeOffset.Now);
        NextStepText.Text = c.NextStep is { Length: > 0 } next ? Loc.T("rc.next", next) : Loc.T("rc.next_none", _keys.Memo);
        ResumeText.Text = Loc.T("rc.before", c.SubtaskName, T(c.PausedSec))
                          + (c.ResumeNote is { Length: > 0 } n ? Loc.T("rc.progress", n) : "")
                          + (c.Resources.Count > 0 ? Loc.T("rc.resources", string.Join(", ", c.Resources.Take(8))) : "");
    }

    private void AutoHide(UIElement panel)
    {
        _autoHide?.Stop();
        var sec = Host.Session?.State.Params["resume_card_auto_close_sec"] ?? 60;
        _autoHide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(sec) };
        _autoHide.Tick += (_, _) => { panel.Visibility = Visibility.Collapsed; _autoHide?.Stop(); EndTemporaryFull(); };
        _autoHide.Start(); // [仮案-24] 入力が始まったら畳む動作は未実装
    }

    private async void RestoreWork_Click(object sender, RoutedEventArgs e) => await Guarded(async () =>
    {
        ResumeCardPanel.Visibility = Visibility.Collapsed;
        await Host.RestoreWorkWindowsAsync();
        EndTemporaryFull();
    });

    private void CloseCard_Click(object sender, RoutedEventArgs e) { ResumeCardPanel.Visibility = Visibility.Collapsed; EndTemporaryFull(); }

    private async void Suspend_Click(object sender, RoutedEventArgs e) => await Guarded(async () =>
    {
        await Host.SuspendAsync();
        EnterStartMode();
    });

    private async void End_Click(object sender, RoutedEventArgs e) => await Guarded(EndFlowAsync);

    /// <summary>終了: S-06 で閉じるものを選ぶ → WM_CLOSE → 配置を戻す → 振り返り(F-10)。</summary>
    private async Task EndFlowAsync()
    {
        ShowFullTemporarily("end_flow");
        var candidates = await Host.EndAsync();
        var toClose = CloseDialog.Ask(this, candidates);
        var review = await Host.FinishAsync(toClose);
        // 先に開始モードへ戻し、振り返りはこのウィンドウを所有者にして出す。
        // 以前は、所有者なしの振り返りが他のウィンドウの後ろに隠れ、その間ミニパネルが「実行中」の見た目のまま
        // 操作できてしまい(中身は閉じ済み)、押すと NullReferenceException になった(実機 L-13 で発生)
        EnterStartMode();
        FcDialog.Info(this, review, Loc.T("review.title"));
    }

    /// <summary>ボタン処理の例外でアプリを落とさない(R-12)。ログに残し、利用者に伝える。</summary>
    private async Task Guarded(Func<Task> action)
    {
        try { await action(); }
        catch (CommandRejectedException ex) { Host.Trace?.Invoke("rejected: " + ex.Message); }
        catch (Exception ex)
        {
            Host.Log.Error("UI", "operation failed", ex);
            FcDialog.Info(this, Loc.T("err.generic", ex.Message));
        }
    }

    // ---------------- 閉じる ----------------

    /// <summary>実行中に閉じるときは、終了・中断(後日再開)・キャンセルを選ばせる(R-14)。処理が終わってから閉じる。</summary>
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed) return;
        e.Cancel = true;
        if (_closing) return;   // 閉じる処理の途中でもう一度 × が押されても、確認や保存を重ねない
        // コンパクト表示(メインは隠れている)で閉じる要求を受けたら、先に元の表示に戻す。
        // Closing イベントの内側では Show() できない(WPF が例外にする)ので、イベントを抜けてから戻し、そのあとで閉じる処理をやり直す
        if (_view != ViewMode.Full && !_tempFull)
        {
            ShowFullTemporarilyLater("closing", then: () => Close());
            return;
        }
        _closing = true;
        _exitCard?.Close();
        if (Host.IsRunning)
        {
            var r = FcDialog.Ask(this, Loc.T("close.text"),
                Loc.T("close.title"), [(Loc.T("close.cancel"), MessageBoxResult.Cancel, false), (Loc.T("close.end"), MessageBoxResult.Yes, false), (Loc.T("close.suspend"), MessageBoxResult.No, true)],
                defaultResult: MessageBoxResult.No, cancelResult: MessageBoxResult.Cancel);   // 既定(Enter)は記録を失わない「中断して閉じる」
            if (r == MessageBoxResult.Cancel) { _closing = false; EndTemporaryFull(); return; }
            await Guarded(async () => { if (r == MessageBoxResult.Yes) await EndFlowAsync(); else await Host.SuspendAsync(); });
        }
        if (!Host.IsRunning) SaveDraftIfAny();   // 開始準備の入力を残す(次の起動でプルダウンに出る)
        // NaN / 無限大は JSON に書けず保存全体が失敗する。有限のときだけ位置を更新し、そうでなければ状態を残す
        RememberBounds();
        var saved = MainBounds();
        if (double.IsFinite(saved.L) && double.IsFinite(saved.T)) { Host.Settings.PanelLeft = saved.L; Host.Settings.PanelTop = saved.T; }
        else Host.Log.Warn("UI", $"window position is not finite: left={Left} top={Top} state={WindowState} visible={IsVisible}");
        try { Host.Settings.Save(Host.DataRoot); } catch (Exception ex) { Host.Log.Error("UI", "settings save failed", ex); }
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethodsPublic.UnregisterKey(_hwnd, HotkeyInterruptId);
            NativeMethodsPublic.UnregisterKey(_hwnd, HotkeyMemoId);
            NativeMethodsPublic.UnregisterKey(_hwnd, HotkeyCompactId);
            NativeMethodsPublic.UnregisterKey(_hwnd, HotkeyLanguageId);
            NativeMethodsPublic.UnregisterSessionNotification(_hwnd);
        }
        _guide?.End("closed");
        await Host.DisposeAsync();
        _hud?.Close();   // 閉じ忘れると、コンパクト表示が残ってプロセスが終わらない
        _tray?.Dispose();   // 先にアイコンを消さないと、プロセスが終わってもトレイに残る
        _closeConfirmed = true;
        // DisposeAsync が同期的に終わると、ここはまだ Closing イベントの内側で、Close() は InvalidOperationException になる。
        // イベントを抜けてから閉じる(実機でアイドル状態から閉じるたびに「予期しないエラー」になっていた)
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }
}
