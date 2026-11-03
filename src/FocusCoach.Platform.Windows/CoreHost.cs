using System.IO;
using FocusCoach.Core;
using FocusCoach.Core.Workspace;
using FocusCoach.Storage;

// [仮案-20] UI と Core の接続役は、XAML を含まないためクラウド(Linux)でもコンパイル確認できる Platform.Windows に置く。
// 依存の向きとしては別プロジェクト(Host)に分けるのが自然だが、プロトタイプでは増やさない。
namespace FocusCoach.Platform.Windows;

/// <summary>出口カード(S-11a)の入力。DueChoice は today / tomorrow / none / yyyy-MM-dd。</summary>
public sealed record ExitCardInput(string Category, string Title, bool Followup, string FollowupText, string DueChoice, bool Tidy);

/// <summary>割り込みを閉じた結果。WorkWindow は割り込みの直前に使っていた作業ウィンドウ(無ければ Zero)。</summary>
public sealed record InterruptCloseResult(IReadOnlyList<CoreEvent> Events, IntPtr WorkWindow, double InterruptSec);

/// <summary>戻ったときの問いかけ(09章 §9.4.1a)。Target は away_reclassify の target。</summary>
public sealed record AwayPrompt(string Target, string Kind, double Minutes);

/// <summary>
/// UI と Core の接続役。UI のボタン・ホットキー・OS監視はすべて CoreCommand を通す(08章 §8.1-3)。
/// 1つの実行(run)の開始から終了・中断までを受け持つ。実行が変わるたびに Loop / Writer / 監視を作り直す(R-33)。
/// 【要ローカル実機確認】クラウドではコンパイルのみ。
/// </summary>
public sealed class CoreHost : IAsyncDisposable
{
    private readonly IWindowSystem _ws = new Win32WindowSystem();
    private readonly List<IActivitySource> _monitors;
    private JsonlEventWriter? _writer;
    private IReadOnlyList<WindowInfo> _startSnapshot = [];
    private IReadOnlyList<WindowInfo> _workSnapshot = [];
    private readonly List<string> _monitorWarnings = [];

    public CoreHost(string? dataRoot = null)
    {
        // FOCUSCOACH_DATA: 実機確認や試用で、実際の記録(DB・JSONL)と混ぜないための保存先の上書き
        dataRoot ??= Environment.GetEnvironmentVariable("FOCUSCOACH_DATA") is { Length: > 0 } env ? env : null;
        DataRoot = dataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusCoach");
        Directory.CreateDirectory(DataRoot);
        Log = new AppLog(DataRoot);
        Log.DeleteOlderThan(14);
        // 起動のたびに1行出す。どのバージョンの exe が動いたかをログで確かめられるようにする(古い exe を動かしていたことがあった)。
        // 保存先のパスは出さない(08 §8.4)。上書きされているかどうかだけを出す
        Log.Info("App", $"started version={AppInfo.Version} formula=v{AppInfo.FormulaVersion} data_override={(dataRoot is not null ? "yes" : "no")}");
        Settings = AppSettings.Load(DataRoot);
        var activity = new ActivityMonitor();
        var explorer = new ExplorerFolderMonitor();
        activity.Faulted += OnMonitorFaulted;
        explorer.Faulted += OnMonitorFaulted;
        _monitors = [activity, explorer];
    }

    public string DataRoot { get; }
    public AppLog Log { get; }
    public AppSettings Settings { get; }
    public Session? Session { get; private set; }
    public EventLoop? Loop { get; private set; }
    public bool DryRun => Settings.WorkspaceDryRun;

    /// <summary>デバッグ欄への出力(UI スレッド以外から呼ばれる)。</summary>
    public Action<string>? Trace { get; set; }
    /// <summary>離席から戻ったときの問いかけ(UI に出す)。</summary>
    public event Action<AwayPrompt>? AwayPromptRequested;
    /// <summary>新しい実行の EventLoop ができたとき(UI は SnapshotChanged を購読し直す)。</summary>
    public event Action<EventLoop>? LoopCreated;

    public bool IsRunning => Session?.State.Status == RunStatus.Running;
    private string DbPath => Path.Combine(DataRoot, "focuscoach.db");

    private void OnMonitorFaulted(string name, Exception ex)
    {
        lock (_monitorWarnings) _monitorWarnings.Add(name);
        Log.Warn(name, $"monitor stopped after repeated failures: {ex.GetType().Name}: {ex.Message}");
        Trace?.Invoke(Loc.T("trace.monitor_stopped", name));
    }

    // ---------------- ガイドと再開候補 ----------------

    public GuidePlan LoadGuide(string category)
    {
        if (!File.Exists(DbPath)) return Guide.Build([], new ParamRegistry());
        using var db = new FocusCoachDb(DbPath);
        return Guide.Build(db.LoadHistory(category), new ParamRegistry());
    }

    /// <summary>同じ種別で前回書いた「最初の一手」(05 F-02、I-05)。開始準備の初期値に使う。</summary>
    public string? LoadLastFirstStep(string category)
    {
        if (!File.Exists(DbPath)) return null;
        try { using var db = new FocusCoachDb(DbPath); return db.LoadLastFirstStep(category); }
        catch (Exception ex) { Log.Error("Storage", "first step load failed", ex); return null; }
    }

    /// <summary>開始準備のプルダウンに出すタスク(終了した実行があるもの、手順を登録したもの)。</summary>
    public List<CategoryInfo> ListCategories()
    {
        if (!File.Exists(DbPath)) return [];
        try { using var db = new FocusCoachDb(DbPath); return db.ListCategories(); }
        catch (Exception ex) { Log.Error("Storage", "category list failed", ex); return []; }
    }

    /// <summary>
    /// タスクを選んだときに入れる手順(05 F-01・F-02)。登録した手順 → 前回の下書きの手順 → 最後に終了した実行の手順、の順。
    /// どれも無ければ null(画面の手順はそのまま)。
    /// </summary>
    public (List<StepItem> Steps, StepSource Source)? LoadSteps(string category)
    {
        if (!File.Exists(DbPath) || string.IsNullOrWhiteSpace(category)) return null;
        try
        {
            using var db = new FocusCoachDb(DbPath);
            var name = category.Trim();
            var t = db.LoadTemplate(name);
            if (t.Count > 0) return (t, StepSource.Registered);
            var d = db.LoadDraft(name);
            if (d is { Steps.Count: > 0 }) return (d.Steps, StepSource.Draft);
            var last = db.LoadLastRunSteps(name);
            return last.Count > 0 ? (last, StepSource.LastRun) : null;
        }
        catch (Exception ex) { Log.Error("Storage", "step load failed", ex); return null; }
    }

    /// <summary>開始準備の入力を下書きとして保存する(開始・閉じる・手順の決定のとき)。失敗しても作業は止めない。</summary>
    public void SaveDraft(TaskDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Category)) return;
        try
        {
            using var db = new FocusCoachDb(DbPath);
            db.SaveDraft(draft);
            Log.Info("Storage", $"draft saved steps={draft.Steps.Count}");
        }
        catch (Exception ex) { Log.Error("Storage", "draft save failed", ex); }
    }

    /// <summary>そのタスクの下書き。無ければ null。</summary>
    public TaskDraft? LoadDraft(string category)
    {
        if (!File.Exists(DbPath) || string.IsNullOrWhiteSpace(category)) return null;
        try { using var db = new FocusCoachDb(DbPath); return db.LoadDraft(category); }
        catch (Exception ex) { Log.Error("Storage", "draft load failed", ex); return null; }
    }

    /// <summary>プルダウンの一覧から外す(記録は消さない)。</summary>
    public void ArchiveCategory(string category)
    {
        using var db = new FocusCoachDb(DbPath);
        db.Archive(category);
        Log.Info("Storage", "category archived");
    }

    /// <summary>手順エディタで「このタスクの手順として登録する」を選んだとき(05 F-01)。</summary>
    public void SaveTemplate(string category, IReadOnlyList<StepItem> steps)
    {
        using var db = new FocusCoachDb(DbPath);
        db.SaveTemplate(category.Trim(), steps);
        Log.Info("Storage", $"step template saved steps={steps.Count}");
    }

    /// <summary>起動時の再開候補(F-15 持ち越し、F-16 異常終了)。</summary>
    public List<RecoverableRun> FindRecoverable()
    {
        try { return RecoveryScanner.Find(DataRoot); }
        catch (Exception ex) { Log.Error("Storage", "recovery scan failed", ex); return []; }
    }

    // ---------------- 実行の開始・再開 ----------------

    public async Task StartAsync(string category, string project, string subtasks, string taskApps, string firstStep = "")
    {
        await CloseCurrentAsync();
        var runId = DateTimeOffset.Now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..4]; // [仮案-22]
        OpenRun(new Session(runId, new SystemClock(), OpenWriter(runId)));

        _startSnapshot = _ws.Enumerate();
        await Loop!.SendAsync(CoreCommand.Of(Cmd.StartTask, ("category", category), ("project", project), ("subtasks", subtasks), ("task_apps", taskApps), ("first_step", firstStep)));
        await RecordSnapshotAsync("start", _startSnapshot);
        await RunWorkspaceAsync(WorkspacePlanner.PlanStart(_startSnapshot), "start");   // F-03
        StartMonitors();
        UpdateDbHeader();
        Log.Info("Core", $"task started run={runId}");
    }

    /// <summary>中断中・異常終了した実行を、同じ記録の続きとして再開する(F-15、F-16)。</summary>
    public async Task<IReadOnlyList<WsStep>> ResumeAsync(RecoverableRun run)
    {
        await CloseCurrentAsync();
        var events = JsonlEventReader.Read(run.Path);
        var writer = new JsonlEventWriter(run.Path); // 書きかけの末尾行を修復してから追記する
        OpenRun(Session.Restore(events, new SystemClock(), writer), writer);
        var reason = run.Kind == "suspended" ? "next_day" : "crash";
        await Loop!.SendAsync(CoreCommand.Of(Cmd.ResumeTask, ("reason", reason)));
        _startSnapshot = RecoveryPlanner.Deserialize(Session!.State.WsSnapshots.GetValueOrDefault("start").WindowsJson ?? "[]");
        StartMonitors();
        UpdateDbHeader();
        Log.Info("Core", $"task resumed run={run.RunId} reason={reason}");
        // 同じ Windows の起動中なら、最小化したまま残ったウィンドウを戻す計画を返す(UI が確認してから実行する)
        return reason == "crash"
            ? RecoveryPlanner.PlanRestoreAfterCrash(Session.State, NativeMethodsPublic.BootId(), _ws.Enumerate())
            : [];
    }

    /// <summary>再開せず、最後のイベントの時刻で終了する(F-16 の2)。</summary>
    public async Task<string> EndRecoverableAsync(RecoverableRun run)
    {
        await CloseCurrentAsync();
        var events = JsonlEventReader.Read(run.Path);
        using var writer = new JsonlEventWriter(run.Path);
        var s = Session.Restore(events, new SystemClock(), writer);
        s.Dispatch(new CoreCommand(Cmd.EndTask, new Dictionary<string, string>(), s.State.LastEventAt));
        return SaveAndReview(s.State);
    }

    /// <summary>破棄: 記録を sessions/discarded/ に移す(削除はしない)。[仮案-29]</summary>
    public void DiscardRecoverable(RecoverableRun run)
    {
        var dir = Path.Combine(DataRoot, "sessions", "discarded");
        Directory.CreateDirectory(dir);
        File.Move(run.Path, Path.Combine(dir, Path.GetFileName(run.Path)), overwrite: true);
        using var db = new FocusCoachDb(DbPath);
        db.MarkInterrupted(run.RunId);
        Log.Info("Core", $"run discarded run={run.RunId}");
    }

    public async Task<int> RestoreWindowsAsync(IReadOnlyList<WsStep> steps)
    {
        var res = await RunWorkspaceAsync(steps, "recover");
        return res.Count(r => r.Done && r.Step.Action != WsAction.Keep);
    }

    private JsonlEventWriter OpenWriter(string runId) => _writer = new JsonlEventWriter(JsonlEventWriter.PathFor(DataRoot, runId));

    private void OpenRun(Session session, JsonlEventWriter? writer = null)
    {
        if (writer is not null) _writer = writer;
        Session = session;
        Loop = new EventLoop(session, new SystemClock());
        Loop.CommandRejected += (c, ex) => { Log.Warn("Core", $"rejected {c.Type}: {ex.Message}"); Trace?.Invoke($"rejected {c.Type}: {ex.Message}"); };
        Loop.SubscriberFailed += ex => Log.Error("UI", "subscriber failed", ex);
        Loop.EventsEmitted += OnEvents;
        LoopCreated?.Invoke(Loop);
    }

    private void StartMonitors()
    {
        lock (_monitorWarnings) _monitorWarnings.Clear();
        foreach (var m in _monitors) m.Start(c => Loop?.Post(c), Session!.State.Params);
    }

    private void StopMonitors() { foreach (var m in _monitors) m.Stop(); }

    /// <summary>イベントを見て、離席からの問いかけを出す(09章 §9.4.1a)。EventLoop のスレッドで呼ばれる。</summary>
    private void OnEvents(IReadOnlyList<CoreEvent> evs)
    {
        var s = Session!.State;
        foreach (var e in evs)
        {
            Trace?.Invoke($"#{e.Seq} {e.Type}");
            if (e.Type == Ev.IdleEnded && s.IdleSpans.LastOrDefault() is { End: { } end } span && span.End == e.Ts)
            {
                var min = (end - span.Start).TotalMinutes;
                if (min * 60 >= s.Params["away_prompt_sec"]) AwayPromptRequested?.Invoke(new AwayPrompt("idle", "idle", min));
            }
            else if (e.Type == Ev.Resumed && e.Get("auto") == "yes")
            {
                var p = s.Pauses.LastOrDefault(x => x.End == e.Ts);
                if (p is not null) AwayPromptRequested?.Invoke(new AwayPrompt("pause", p.Reason, (e.Ts - p.Start).TotalMinutes));
            }
            else if (e.Type is Ev.Paused or Ev.TaskSuspended or Ev.SessionResumed or Ev.TaskEnded)
            {
                Log.Info("Core", $"{e.Type} {string.Join(' ', e.Data.Where(kv => kv.Key is "reason" or "auto").Select(kv => kv.Key + "=" + kv.Value))}");
            }
        }
    }

    // ---------------- 作業中の操作 ----------------

    /// <summary>実行が閉じた後(終了・中断の直後)に届いた操作は、例外ではなく「拒否」にする(UI は静かに無視する)。</summary>
    public Task<IReadOnlyList<CoreEvent>> SendAsync(CoreCommand cmd) =>
        Loop?.SendAsync(cmd) ?? throw new CommandRejectedException(Loc.T("err.no_task"));

    /// <summary>F9 / ⚡: 作業中なら割り込み(ワンクリックで一時停止)、一時停止中なら再開(09章 §9.4.1)。</summary>
    public async Task<IReadOnlyList<CoreEvent>> ToggleInterruptAsync()
    {
        if (Session?.State is not { Status: RunStatus.Running } s) return [];
        if (s.IsPaused) return await Loop!.SendAsync(CoreCommand.Of(Cmd.Resume));
        _workSnapshot = _ws.Enumerate().Where(w => WorkspacePlanner.IsCandidate(w) && !w.Minimized).ToList();
        var evs = await Loop!.SendAsync(CoreCommand.Of(Cmd.Interrupt, ("kind", "now")));
        await RecordSnapshotAsync("interrupt", _workSnapshot);
        return evs;
    }

    /// <summary>F10 / 📝(作業中): 棚上げ(09章 §9.4.1)。</summary>
    public Task<IReadOnlyList<CoreEvent>> DeferAsync(string text) =>
        SendAsync(CoreCommand.Of(Cmd.Interrupt, ("kind", "defer"), ("note", text)));

    /// <summary>F10 / 📝(割り込み中)、復帰カードの [メモを直す]: 再開メモ「どこまで / 次の一手」(I-01)。空の行は変更しない。</summary>
    public Task<IReadOnlyList<CoreEvent>> ResumeNoteAsync(string done, string next) =>
        SendAsync(CoreCommand.Of(Cmd.InterruptNote, ("text", done), ("next", next)));

    public Task<IReadOnlyList<CoreEvent>> ReclassifyAsync(string target) =>
        SendAsync(CoreCommand.Of(Cmd.AwayReclassify, ("target", target)));

    /// <summary>[作業画面に戻す](09章 §9.4.2)。中断中に開いたウィンドウは最小化するだけで、閉じない。</summary>
    public Task RestoreWorkWindowsAsync() =>
        RunWorkspaceAsync(WorkspacePlanner.PlanResume(_workSnapshot, _ws.Enumerate()), "resume");

    // ---------------- 割り込みの出口・再入場・あとでやること(design-0.12A-interrupt-closure.md) ----------------

    /// <summary>しおりを挟んで割り込み対応中か(F9 / ボタンの理由が interrupt の一時停止)。</summary>
    public bool IsInterrupted => Session?.State is { Status: RunStatus.Running, OpenPause.Reason: "interrupt" };

    /// <summary>割り込み対応を始めてからの秒数(対応中でなければ 0)。</summary>
    public double InterruptElapsedSec => IsInterrupted ? (DateTimeOffset.Now - Session!.State.OpenPause!.Start).TotalSeconds : 0;

    private long? _lastInterruptId;

    /// <summary>出口カードのカテゴリのプルダウン(最近使った順)。DB が無い・読めないときは既定のカテゴリだけ。</summary>
    public List<string> ListInterruptCategories()
    {
        try { using var db = new FocusCoachDb(DbPath); return db.ListInterruptCategories() is { Count: > 0 } l ? l : [InterruptCategories.Default]; }
        catch (Exception ex) { Log.Error("Storage", "interrupt category list failed", ex); return [InterruptCategories.Default]; }
    }

    public void ArchiveInterruptCategory(string name)
    {
        using var db = new FocusCoachDb(DbPath);
        db.ArchiveInterruptCategory(name);
        Log.Info("Storage", "interrupt category archived");
    }

    /// <summary>出口カードの「片付ける」で最小化する予定のウィンドウの数(割り込み中に開いた・前面に出したもの)。</summary>
    public int PreviewTidyCount()
    {
        try { return WorkspacePlanner.PlanResume(_workSnapshot, _ws.Enumerate()).Count(s => s.Action == WsAction.Minimize); }
        catch (Exception ex) { Log.Error("Workspace", "tidy preview failed", ex); return 0; }
    }

    /// <summary>
    /// 割り込みを閉じる(出口カード S-11a)。Core に記録し、片付け(作業画面に戻す)を行い、DB に割り込みの記録とあとでやることを保存する。
    /// via=away は、離席を割り込みに分類し直した直後(一時停止は既に終わっている。片付けはしない)。
    /// </summary>
    public async Task<InterruptCloseResult> CloseInterruptAsync(ExitCardInput input, int guideSec, string via = "f9")
    {
        if (Session?.State is not { Status: RunStatus.Running } s) throw new CommandRejectedException(Loc.T("err.no_task"));
        var pause = via == "f9" ? s.OpenPause : s.Pauses.LastOrDefault(p => p.Reason == "interrupt" && p.End is not null);
        var startedAt = pause?.Start ?? DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var due = input.Followup ? Followups.ParseDueChoice(input.DueChoice, today) : null;
        var tidy = input.Tidy && via == "f9";
        var evs = await Loop!.SendAsync(CoreCommand.Of(Cmd.InterruptClose, ("via", via), ("category", input.Category), ("title", input.Title),
            ("outcome", input.Followup ? "followup" : "done"), ("followup", input.FollowupText), ("due", Followups.FormatDue(due)),
            ("tidy", tidy ? "yes" : "no"), ("guide_sec", guideSec.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        var closed = s.InterruptCloses[^1];

        string? windowsJson = null;
        if (tidy)
        {
            var res = await RunWorkspaceAsync(WorkspacePlanner.PlanResume(_workSnapshot, _ws.Enumerate()), "resume");
            var parked = res.Where(r => r.Step.Action == WsAction.Minimize).Select(r => new { process = r.Step.Window.Process, title = r.Step.Window.Title }).ToList();
            if (parked.Count > 0) windowsJson = System.Text.Json.JsonSerializer.Serialize(parked);
        }

        // タスクの終了を待たずに保存する(異常終了しても、あとでやることが残るように)。失敗しても作業は止めない(JSONL が正)
        try
        {
            using var db = new FocusCoachDb(DbPath);
            var (iid, fid) = db.SaveInterruptClose(new InterruptCloseRecord(s.RunId, startedAt, closed.At, closed.Category, closed.Title, closed.Outcome,
                closed.Followup, due, s.Category, windowsJson));
            _lastInterruptId = iid;
            Log.Info("Storage", $"interrupt closed outcome={closed.Outcome} followup={(fid is not null ? "yes" : "no")} sec={closed.InterruptSec:0} guide={guideSec}");
        }
        catch (Exception ex) { Log.Error("Storage", "interrupt close save failed", ex); _lastInterruptId = null; }

        Settings.LastInterruptCategory = closed.Category;
        try { Settings.Save(DataRoot); } catch (Exception ex) { Log.Error("UI", "settings save failed", ex); }
        return new InterruptCloseResult(evs, LastWorkWindow(startedAt), closed.InterruptSec);
    }

    /// <summary>再入場ガイドの終わりを記録する(how: timeout / now / interrupt / closed)。</summary>
    public async Task EndReentryAsync(string how, double shownSec, int extends)
    {
        if (!IsRunning) return;
        await Loop!.SendAsync(CoreCommand.Of(Cmd.ReentryEnd, ("how", how),
            ("shown_sec", shownSec.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)), ("extends", extends.ToString())));
        if (_lastInterruptId is { } id)
        {
            try { using var db = new FocusCoachDb(DbPath); db.SetReentry(id, how, shownSec); }
            catch (Exception ex) { Log.Error("Storage", "reentry save failed", ex); }
        }
        Log.Info("UI", $"reentry ended how={how} shown={shownSec:0}s extends={extends}");
    }

    /// <summary>今の(または直前に閉じた)割り込みの、直前に使っていた作業ウィンドウ。無ければ Zero。</summary>
    public IntPtr InterruptWorkWindow(string via = "f9")
    {
        if (Session?.State is not { } s) return IntPtr.Zero;
        var start = via == "f9" ? s.OpenPause?.Start : s.Pauses.LastOrDefault(p => p.Reason == "interrupt" && p.End is not null)?.Start;
        return LastWorkWindow(start ?? DateTimeOffset.Now);
    }

    /// <summary>割り込みの直前に使っていた作業ウィンドウ(再入場ガイドの置き場所と、戻すフォーカスの先)。無ければ Zero。</summary>
    private IntPtr LastWorkWindow(DateTimeOffset before)
    {
        var s = Session?.State;
        var hwnd = s?.Foreground.LastOrDefault(f => f.TaskWindow && f.Start <= before && f.Hwnd.Length > 0)?.Hwnd
                   ?? _workSnapshot.FirstOrDefault()?.Hwnd;
        try { return hwnd is { Length: > 0 } ? Win32WindowSystem.ToHandle(hwnd) : IntPtr.Zero; }
        catch (FormatException) { return IntPtr.Zero; }
    }

    /// <summary>あとでやること(未完了 + 7日以内に完了したもの)。</summary>
    public List<FollowupItem> ListFollowups()
    {
        if (!File.Exists(DbPath)) return [];
        try { using var db = new FocusCoachDb(DbPath); return db.ListFollowups(DateTimeOffset.Now); }
        catch (Exception ex) { Log.Error("Storage", "followup list failed", ex); return []; }
    }

    public void SetFollowupStatus(long id, string status)
    {
        using var db = new FocusCoachDb(DbPath);
        db.SetFollowupStatus(id, status, DateTimeOffset.Now);
        Log.Info("Storage", $"followup {status}");
    }

    /// <summary>ウィンドウメッセージ(ロック・リモート切断・スリープ)を受け取る。App のウィンドウプロシージャから呼ぶ。</summary>
    public void OnWindowMessage(int msg, long wParam)
    {
        // メッセージを受けた時刻を付ける。スリープに入る前にイベントループが処理しきれないと、
        // 復帰後の時刻で記録されてスリープの区間が一時停止にならないため(時刻の前後は origin=monitor で Core が整える)
        if (WindowsSessionEvents.Translate(msg, wParam) is { } cmd && Loop is not null) Loop.Post(cmd with { At = DateTimeOffset.Now });
    }

    // ---------------- 中断・終了 ----------------

    /// <summary>中断して後日再開(持ち越し。F-15)。ウィンドウは元に戻す。</summary>
    public async Task SuspendAsync()
    {
        if (!IsRunning) return;
        StopMonitors();
        await Loop!.SendAsync(CoreCommand.Of(Cmd.SuspendTask));
        await RestoreStartWindowsAsync();
        UpdateDbHeader();
        Log.Info("Core", $"task suspended run={Session!.RunId}");
        await CloseCurrentAsync();
    }

    /// <summary>
    /// 終了の前半: タスクを終了し、閉じる候補(S-06)を返す。UI が利用者に選ばせてから FinishAsync を呼ぶ。
    /// 作業中に開いたウィンドウを最小化することはしない(R-36)。
    /// </summary>
    public async Task<IReadOnlyList<WsStep>> EndAsync()
    {
        // 終了処理の途中(振り返りの表示中など)にもう一度押されても落とさない
        if (Loop is null || !IsRunning) throw new CommandRejectedException(Loc.T("err.no_task"));
        StopMonitors();
        await Loop.SendAsync(CoreCommand.Of(Cmd.EndTask));
        var state = Session!.State;
        var alive = _ws.Enumerate().Select(w => w.Hwnd).ToHashSet();
        var opened = state.OpenedDuring.Where(o => o.ClosedAt is null && alive.Contains(o.Hwnd))
                                       .Select(o => new WindowInfo(o.Hwnd, o.Process, o.Title)).ToList();
        return WorkspacePlanner.PlanFinish(opened, state.Project, state.Foreground.LastOrDefault(f => f.Process.Length > 0 && !f.Process.StartsWith("FocusCoach", StringComparison.OrdinalIgnoreCase))?.Hwnd);
    }

    /// <summary>終了の後半: 選ばれたウィンドウに WM_CLOSE を送り、開始時の配置に戻し、保存して振り返りを返す。</summary>
    public async Task<string> FinishAsync(IEnumerable<WindowInfo> toClose)
    {
        await RunWorkspaceAsync(toClose.Select(w => new WsStep(w, WsAction.Close, "selected in S-06")).ToList(), "close", allowWhileEnded: true);
        await RestoreStartWindowsAsync();
        var review = SaveAndReview(Session!.State);
        await CloseCurrentAsync();
        return review;
    }

    private string SaveAndReview(RunState state)
    {
        var metrics = MetricsCalculator.Compute(state, state.EndedAt ?? state.LastEventAt);
        using var db = new FocusCoachDb(DbPath);
        var history = db.LoadHistory(state.Category);
        db.SaveRun(state, metrics);
        var cmp = Compare.Build(RunSummary.From(state, metrics), history, state.Params);
        List<string> warnings;
        lock (_monitorWarnings) warnings = [.. _monitorWarnings];
        Log.Info("Core", $"task saved run={state.RunId} active={metrics.ActiveSec:0}s");
        return Review.Format(metrics, cmp, state.Deferred, warnings, state.InterruptCloses);
    }

    /// <summary>開始時に FocusCoach が最小化したウィンドウだけを、元の配置に戻す(F-10 の 4)。</summary>
    private Task RestoreStartWindowsAsync()
    {
        var before = _startSnapshot.Where(w => WorkspacePlanner.IsCandidate(w) && !w.Minimized).ToDictionary(w => w.Hwnd);
        var steps = _ws.Enumerate().Where(w => before.ContainsKey(w.Hwnd) && w.Minimized)
                       .Select(w => new WsStep(w with { Placement = before[w.Hwnd].Placement }, WsAction.Restore, "restore at finish")).ToList();
        return RunWorkspaceAsync(steps, "restore", allowWhileEnded: true);
    }

    private void UpdateDbHeader()
    {
        try { using var db = new FocusCoachDb(DbPath); db.UpsertRunHeader(Session!.State); }
        catch (Exception ex) { Log.Error("Storage", "run header update failed", ex); } // DB の失敗で作業は止めない(JSONL が正)
    }

    /// <summary>異常終了の直前に呼ぶ(08 §8.7)。状態を interrupted にし、クラッシュログを書く。</summary>
    public string OnCrash(Exception ex)
    {
        var s = Session?.State;
        var summary = s is null ? "no run" : $"run={s.RunId} status={s.Status} subtask={s.ActiveIndex + 1}/{s.Subtasks.Count} events={s.EventCount}";
        try { if (s is { Status: RunStatus.Running }) { using var db = new FocusCoachDb(DbPath); db.MarkInterrupted(s.RunId); } }
        catch { /* 異常終了中なので握りつぶす */ }
        return Log.WriteCrash(ex, summary);
    }

    private async Task RecordSnapshotAsync(string phase, IReadOnlyList<WindowInfo> windows)
    {
        var candidates = windows.Where(WorkspacePlanner.IsCandidate).ToList();
        await Loop!.SendAsync(CoreCommand.Of(Cmd.WsSnapshot, ("phase", phase), ("boot_id", NativeMethodsPublic.BootId()),
            ("windows", RecoveryPlanner.Serialize(candidates)), ("count", candidates.Count.ToString())));
    }

    /// <summary>計画を実行し、結果を ws_* イベントとして記録する(R-11)。</summary>
    private async Task<IReadOnlyList<WsResult>> RunWorkspaceAsync(IReadOnlyList<WsStep> steps, string phase, bool allowWhileEnded = false)
    {
        var dry = DryRun && phase != "close"; // 閉じるのは利用者が S-06 で選んだものだけなので、ドライランでも実行する [仮案-30]
        var res = WorkspaceExecutor.Execute(steps, _ws, dry);
        Trace?.Invoke($"workspace {phase}: {res.Count(r => r.Step.Action != WsAction.Keep)} windows (dryRun={dry})");
        foreach (var r in res.Where(r => r.Step.Action != WsAction.Keep))
        {
            Log.Info("Workspace", $"{phase} {r.Step.Action} {r.Step.Window.Process} {LogMask.Mask("title", r.Step.Window.Title)} -> {r.Detail}");
            if (Loop is null || (!allowWhileEnded && !IsRunning)) continue;
            var op = r.Step.Action switch { WsAction.Minimize => "minimize", WsAction.Restore => "restore", _ => "close" };
            try
            {
                await Loop.SendAsync(CoreCommand.Of(Cmd.WsAction, ("op", op), ("hwnd", r.Step.Window.Hwnd), ("process", r.Step.Window.Process),
                    ("result", r.Detail), ("phase", phase)));
            }
            catch (CommandRejectedException) { /* 終了後の記録は捨てる */ }
        }
        if (res.Any(r => !r.Done)) Trace?.Invoke(Loc.T("trace.ws_failed", res.Count(r => !r.Done)));
        return res;
    }

    private async Task CloseCurrentAsync()
    {
        StopMonitors();
        if (Loop is not null) { await Loop.DisposeAsync(); Loop = null; }
        _writer?.Dispose();
        _writer = null;
        Session = null;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var m in _monitors) m.Dispose();
        await CloseCurrentAsync();
    }
}

/// <summary>振り返り(S-07)の数値のみの簡易版。P-5 で画面にする。</summary>
public static class Review
{
    public static string Format(RunMetrics m, ComparisonResult cmp, IReadOnlyList<string> deferred, IReadOnlyList<string> monitorWarnings,
        IReadOnlyList<InterruptCloseRec>? closes = null)
    {
        static string H(double s) => TimeSpan.FromSeconds(Math.Round(s)).ToString(@"h\:mm\:ss");
        var lines = new List<string>
        {
            Loc.T("review.line1", H(m.TotalSec), H(m.ActiveSec), H(m.PausedSec), H(m.IdleSec)),
            Loc.T("review.line2", m.FocusScore, m.SwitchCount, m.InterruptCount, m.DeferredCount),
            m.WorkDays > 1 ? Loc.T("review.days", m.WorkDays) : (m.SameDay ? Loc.T("review.sameday") : ""),
            "",
        };
        foreach (var r in cmp.Rows)
            lines.Add($"{r.Label}: {H(r.Current)}" + (r.Previous is { } p ? Loc.T("review.prev", H(p)) : ""));
        if (cmp.Improvements.Count > 0) { lines.Add(""); lines.AddRange(cmp.Improvements.Select(x => "• " + x)); }
        if (deferred.Count > 0) { lines.Add(""); lines.Add(Loc.T("review.deferred_head")); lines.AddRange(deferred.Select(x => "  □ " + x)); }
        // この作業中に増えた「あとでやること」(出口カード。design-0.12A §5.2)。一覧はトレイのメニューから開ける
        var follow = (closes ?? []).Where(c => c.Followup.Length > 0).ToList();
        if (follow.Count > 0)
        {
            lines.Add("");
            lines.Add(Loc.T("review.followups_head"));
            lines.AddRange(follow.Select(c => "  □ " + c.Followup + (c.Due.Length > 0 ? Loc.T("review.due", Loc.Date(DateOnly.Parse(c.Due, System.Globalization.CultureInfo.InvariantCulture))) : "")));
        }
        foreach (var w in monitorWarnings) lines.Add(Loc.T("review.monitor_warn", w));
        return string.Join(Environment.NewLine, lines.Where((l, i) => l.Length > 0 || i > 2));
    }
}
