using System.Globalization;

namespace FocusCoach.Core;

public sealed class SessionOptions
{
    public ExclusionRules Exclusions { get; init; } = new();
    public IReadOnlyList<string> InterruptApps { get; init; } = TaskWindowClassifier.DefaultInterruptApps;
    /// <summary>ユーザーがタスク用に指定したアプリ(正規化済みのプロセス名)。</summary>
    public IReadOnlyList<string> TaskApps { get; init; } = [];
}

/// <summary>
/// コマンド → イベント → 状態 の変換器(08章 §8.1)。単一スレッドで使う(EventLoop が保証する)。
/// コマンドの検証はここで行い、不正なら CommandRejectedException。イベントは検証済みの事実だけを持つ。
/// </summary>
public sealed class Session
{
    private readonly IClock _clock;
    private readonly IEventSink? _sink;
    private readonly SessionOptions _opt;
    private long _seq;
    private bool _started;

    public Session(string runId, IClock clock, IEventSink? sink = null, SessionOptions? options = null)
    {
        RunId = runId;
        _clock = clock;
        _sink = sink;
        _opt = options ?? new SessionOptions();
        State = new RunState { RunId = runId };
    }

    /// <summary>
    /// 記録済みのイベント列から Session を復元する(F-16、持ち越し F-15)。seq は続きから振る。
    /// 復元後に resume_task(reason=crash / 中断中なら next_day)を送って再開する。
    /// </summary>
    public static Session Restore(IReadOnlyList<CoreEvent> events, IClock clock, IEventSink? sink = null, SessionOptions? options = null)
    {
        if (events.Count == 0) throw new ArgumentException("no events to restore");
        var runId = events.FirstOrDefault(e => e.Type == Ev.SessionStart)?.Get("run_id") ?? "restored";
        var s = new Session(runId, clock, sink, options);
        foreach (var e in events) Reducer.Apply(s.State, e);
        s._seq = events.Max(e => e.Seq);
        s._started = true;
        return s;
    }

    public string RunId { get; }
    public RunState State { get; }

    public IReadOnlyList<CoreEvent> Dispatch(CoreCommand cmd)
    {
        var at = cmd.At ?? _clock.Now;
        // 時刻が巻き戻るコマンド: OS監視由来(origin=monitor)は直前の時刻に切り上げ、それ以外は拒否する(08 §8.3、R-19)
        if (_started && at < State.LastEventAt)
        {
            if (cmd.Get("origin") == "monitor") at = State.LastEventAt;
            else throw new CommandRejectedException($"time went backwards: {at:O} < {State.LastEventAt:O}");
        }

        var evs = new List<CoreEvent>();
        void Emit(string type, params (string K, string V)[] data) =>
            evs.Add(new CoreEvent(++_seq, at, type, data.ToDictionary(x => x.K, x => x.V)));

        if (!_started)
        {
            var d = new List<(string, string)>
            {
                ("run_id", RunId), ("app_version", AppInfo.Version), ("formula_version", AppInfo.FormulaVersion.ToString()),
            };
            foreach (var (k, v) in State.Params.All) d.Add(("param." + k, v.ToString("R", CultureInfo.InvariantCulture)));
            Emit(Ev.SessionStart, d.ToArray());
            _started = true;
        }

        try { Handle(cmd, at, Emit); }
        catch
        {
            // 検証に失敗したら、このコマンドで積んだイベントは捨てる(session_start だけは残す)
            var keep = evs.Where(e => e.Type == Ev.SessionStart).ToList();
            foreach (var e in keep) Commit(e);
            throw;
        }

        foreach (var e in evs) Commit(e);
        return evs;
    }

    private void Commit(CoreEvent e)
    {
        Reducer.Apply(State, e);
        _sink?.Append(e);
    }

    private void Handle(CoreCommand cmd, DateTimeOffset at, Action<string, (string K, string V)[]> emit)
    {
        void E(string type, params (string K, string V)[] d) => emit(type, d);
        var s = State;

        void RequireRunning()
        {
            if (s.Status != RunStatus.Running) throw new CommandRejectedException($"{cmd.Type}: no running task (status={s.Status})");
        }
        void RequireOpen()
        {
            if (s.Status is not (RunStatus.Running or RunStatus.Suspended)) throw new CommandRejectedException($"{cmd.Type}: no open task");
        }
        string Iso(DateTimeOffset t) => t.ToString("O", CultureInfo.InvariantCulture);

        switch (cmd.Type)
        {
            case Cmd.StartTask:
            {
                if (s.Status == RunStatus.Running) throw new CommandRejectedException("start_task: a task is already running");
                var category = cmd.Get("category");
                if (category.Length == 0) throw new CommandRejectedException("start_task: category is required");
                var data = new List<(string, string)> { ("category", category), ("project", cmd.Get("project")) };
                if (cmd.Get("first_step").Trim() is { Length: > 0 } fs) data.Add(("first_step", fs));
                if (cmd.Has("task_apps")) data.Add(("task_apps", string.Join(',', cmd.Get("task_apps").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(TaskWindowClassifier.NormalizeProcess))));
                if (cmd.Has("target_min")) data.Add(("target_sec", (double.Parse(cmd.Get("target_min"), CultureInfo.InvariantCulture) * 60).ToString("R", CultureInfo.InvariantCulture)));
                emit(Ev.TaskStarted, data.ToArray());
                var list = Reducer.ParseSubtaskList(cmd.Get("subtasks"));
                // [仮案-07] サブタスク定義が無いときは「サブタスク1」を暗黙に作る
                if (list.Count == 0) list.Add((Loc.T("subtask.n", 1), null));
                foreach (var (name, target) in list)
                    E(Ev.SubtaskAdded, target is null ? [("name", name)] : [("name", name), ("target_sec", target.Value.ToString("R", CultureInfo.InvariantCulture))]);
                // イベントはまだ State に反映されていないので index は 0 固定
                E(Ev.SubtaskStarted, ("index", "0"));
                break;
            }

            case Cmd.NextSubtask:
            {
                RequireRunning();
                var cur = s.ActiveIndex;
                if (cur >= 0) E(Ev.SubtaskCompleted, ("index", cur.ToString()));
                var next = NextPendingAfter(cur);
                if (next < 0)
                {
                    // [仮案-08] 次が無いときは「サブタスクN」を自動追加する(その場で追加, F-04)
                    next = s.Subtasks.Count;
                    E(Ev.SubtaskAdded, ("name", cmd.Get("name", Loc.T("subtask.n", next + 1))));
                }
                E(Ev.SubtaskStarted, ("index", next.ToString()));
                break;
            }

            case Cmd.SkipSubtask:
            {
                RequireRunning();
                var cur = s.ActiveIndex;
                if (cur < 0) throw new CommandRejectedException("skip_subtask: no active subtask");
                E(Ev.SubtaskSkipped, ("index", cur.ToString()));
                var next = NextPendingAfter(cur);
                if (next >= 0) E(Ev.SubtaskStarted, ("index", next.ToString()));
                break;
            }

            case Cmd.GotoSubtask:
            {
                RequireRunning();
                var n = int.Parse(cmd.Get("n", "0"), CultureInfo.InvariantCulture) - 1; // DSLは1始まり
                if (n < 0 || n >= s.Subtasks.Count) throw new CommandRejectedException($"goto_subtask: out of range: {n + 1}");
                E(Ev.SubtaskStarted, ("index", n.ToString()));
                break;
            }

            case Cmd.AddSubtask:
                RequireRunning();
                E(Ev.SubtaskAdded, ("name", cmd.Get("name", Loc.T("subtask.default"))));
                break;

            case Cmd.Pause:
                RequireRunning();
                if (s.IsPaused) throw new CommandRejectedException("pause: already paused");
                E(Ev.Paused, ("reason", cmd.Get("reason", "other")));
                break;

            case Cmd.Resume:
            {
                RequireRunning();
                if (!s.IsPaused) throw new CommandRejectedException("resume: not paused");
                var reason = s.OpenPause!.Reason;
                var pausedSec = (at - s.OpenPause.Start).TotalSeconds;
                E(Ev.Resumed, ("reason", reason));
                if (reason == "interrupt")
                    E(Ev.ResumeCardShown,
                        ("subtask", s.Active?.Name ?? ""), ("paused_sec", pausedSec.ToString("R", CultureInfo.InvariantCulture)),
                        ("has_note", (s.Active?.ResumeNote is { Length: > 0 }).ToString().ToLowerInvariant()),
                        ("has_next", (s.Active?.NextStep is { Length: > 0 }).ToString().ToLowerInvariant()));
                break;
            }

            case Cmd.Interrupt:
            {
                RequireRunning();
                var kind = cmd.Get("kind", "now");
                var note = cmd.Get("note", cmd.Get("text"));
                if (kind == "defer")
                {
                    E(Ev.InterruptDeferred, ("text", note));
                }
                else if (kind == "now")
                {
                    E(Ev.InterruptMarked, ("kind", "now"), ("note", note), ("has_note", (note.Length > 0).ToString().ToLowerInvariant()));
                    if (!s.IsPaused) E(Ev.Paused, ("reason", "interrupt"));
                }
                else throw new CommandRejectedException($"interrupt: unknown kind: {kind}");
                break;
            }

            case Cmd.InterruptNote:
            {
                RequireRunning();
                var text = cmd.Get("text").Trim();
                var next = cmd.Get("next").Trim();
                if (text.Length == 0 && next.Length == 0) throw new CommandRejectedException("interrupt_note: text or next is required");
                // 直前の「すぐ対応」の割り込みのサブタスク。無ければ現在のサブタスク(09章 §9.4.1)
                var idx = s.Interrupts.LastOrDefault(i => i.Kind == "now")?.SubtaskIndex ?? (s.ActiveIndex >= 0 ? s.ActiveIndex : null);
                if (idx is null) throw new CommandRejectedException("interrupt_note: no subtask to attach");
                E(Ev.InterruptNoteAdded, ("index", idx.Value.ToString()), ("text", text), ("next", next));
                break;
            }

            case Cmd.InterruptClose:
            {
                RequireRunning();
                var via = cmd.Get("via", "f9");
                double interruptSec;
                if (via == "f9")
                {
                    if (s.OpenPause is not { Reason: "interrupt" } open) throw new CommandRejectedException("interrupt_close: not in an interrupt");
                    interruptSec = (at - open.Start).TotalSeconds;
                }
                else if (via == "away")
                {
                    // 離席を割り込みに分類し直した直後(区間は閉じている)。記録だけで、一時停止は触らない
                    if (s.IsPaused) throw new CommandRejectedException("interrupt_close: paused");
                    var p = s.Pauses.LastOrDefault(x => x.Reason == "interrupt" && x.End is not null)
                            ?? throw new CommandRejectedException("interrupt_close: no interrupt to close");
                    interruptSec = (p.End!.Value - p.Start).TotalSeconds;
                }
                else throw new CommandRejectedException($"interrupt_close: bad via: {via}");

                var outcome = cmd.Get("outcome", "done");
                if (outcome is not ("done" or "followup")) throw new CommandRejectedException($"interrupt_close: bad outcome: {outcome}");
                var followup = outcome == "followup" ? cmd.Get("followup").Trim() : "";
                if (outcome == "followup" && followup.Length == 0) throw new CommandRejectedException("interrupt_close: followup text is required");
                var due = outcome == "followup" ? cmd.Get("due").Trim() : "";
                if (due.Length > 0 && !DateOnly.TryParseExact(due, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    throw new CommandRejectedException($"interrupt_close: bad due: {due}");
                var category = cmd.Get("category").Trim() is { Length: > 0 } c ? c : InterruptCategories.Default;
                var guideSec = (int)Math.Max(0, double.Parse(cmd.Get("guide_sec", "0"), CultureInfo.InvariantCulture));

                E(Ev.InterruptClosed, ("category", category), ("title", cmd.Get("title").Trim()), ("outcome", outcome), ("followup", followup), ("due", due),
                    ("interrupt_sec", interruptSec.ToString("R", CultureInfo.InvariantCulture)), ("via", via),
                    ("tidy", CoreEvent.Truthy(cmd.Get("tidy", "no")) ? "yes" : "no"), ("guide_sec", guideSec.ToString(CultureInfo.InvariantCulture)));
                if (via == "f9")
                {
                    E(Ev.Resumed, ("reason", "interrupt"));
                    // 再入場ガイドを出さないときは、今までどおり復帰カードを出す(design-0.12A §4.3)
                    if (guideSec == 0)
                        E(Ev.ResumeCardShown,
                            ("subtask", s.Active?.Name ?? ""), ("paused_sec", interruptSec.ToString("R", CultureInfo.InvariantCulture)),
                            ("has_note", (s.Active?.ResumeNote is { Length: > 0 }).ToString().ToLowerInvariant()),
                            ("has_next", (s.Active?.NextStep is { Length: > 0 }).ToString().ToLowerInvariant()), ("via", "exit_card"));
                }
                break;
            }

            case Cmd.ReentryEnd:
            {
                RequireRunning();
                var how = cmd.Get("how", "timeout");
                if (how is not ("timeout" or "now" or "interrupt" or "closed")) throw new CommandRejectedException($"reentry_end: bad how: {how}");
                E(Ev.ReentryEnded, ("how", how), ("shown_sec", cmd.Get("shown_sec", "0")), ("extends", cmd.Get("extends", "0")));
                break;
            }

            case Cmd.AwayStart:
            {
                // 監視由来: 実行中でなければ、または既に一時停止中なら何もしない(拒否してログを汚さない)
                if (s.Status != RunStatus.Running || s.IsPaused) return;
                var reason = cmd.Get("reason", "lock");
                if (reason is not ("lock" or "sleep" or "remote")) throw new CommandRejectedException($"away_start: bad reason: {reason}");
                E(Ev.Paused, ("reason", reason), ("auto", "yes"));
                break;
            }

            case Cmd.AwayEnd:
            {
                var reason = cmd.Get("reason", "lock");
                // 自分が始めた一時停止だけを解除する(利用者の割り込み中にロック解除しても、割り込みは続く)
                if (s.Status != RunStatus.Running || s.OpenPause?.Reason != reason) return;
                E(Ev.Resumed, ("reason", reason), ("auto", "yes"));
                break;
            }

            case Cmd.AwayReclassify:
            {
                RequireRunning();
                var target = cmd.Get("target", "idle");
                DateTimeOffset from, to;
                string orig;
                if (target == "idle")
                {
                    var span = s.IdleSpans.LastOrDefault() ?? throw new CommandRejectedException("away_reclassify: no idle span");
                    (from, to, orig) = (span.Start, span.End!.Value, "idle");
                }
                else if (target == "pause")
                {
                    var p = s.Pauses.LastOrDefault(x => x.End is not null && x.Reason is "lock" or "sleep" or "remote")
                            ?? throw new CommandRejectedException("away_reclassify: no lock/sleep/remote pause");
                    (from, to, orig) = (p.Start, p.End!.Value, p.Reason);
                }
                else throw new CommandRejectedException($"away_reclassify: bad target: {target}");
                var idx = s.Subtasks.FirstOrDefault(st => st.Intervals.Any(i => i.Start <= from && (i.End ?? at) >= from))?.Index ?? s.ActiveIndex;
                E(Ev.AwayReclassified, ("target", target), ("from", Iso(from)), ("to", Iso(to)), ("orig", orig), ("index", idx.ToString()));
                E(Ev.ResumeCardShown, ("subtask", idx >= 0 ? s.Subtasks[idx].Name : ""),
                    ("paused_sec", (to - from).TotalSeconds.ToString("R", CultureInfo.InvariantCulture)), ("via", "away"));
                break;
            }

            case Cmd.SuspendTask:
                RequireRunning();
                E(Ev.TaskSuspended, ("reason", cmd.Get("reason", "next_day")));
                break;

            case Cmd.ResumeTask:
            {
                if (s.Status == RunStatus.Suspended)
                    E(Ev.SessionResumed, ("reason", "next_day"), ("gap_sec", (at - (s.OpenPause?.Start ?? at)).TotalSeconds.ToString("R", CultureInfo.InvariantCulture)));
                else if (s.Status == RunStatus.Running && cmd.Get("reason") == "crash")
                    E(Ev.SessionResumed, ("reason", "crash"), ("gap_from", Iso(s.LastEventAt)), ("gap_sec", (at - s.LastEventAt).TotalSeconds.ToString("R", CultureInfo.InvariantCulture)));
                else throw new CommandRejectedException($"resume_task: nothing to resume (status={s.Status})");
                break;
            }

            case Cmd.WsSnapshot:
                RequireOpen();
                E(Ev.WsSnapshot, ("phase", cmd.Get("phase", "start")), ("boot_id", cmd.Get("boot_id")), ("windows", cmd.Get("windows", "[]")),
                    ("count", cmd.Get("count", "")));
                break;

            case Cmd.WsAction:
            {
                // 終了後のウィンドウ整理(S-06 の WM_CLOSE、配置の復元)も記録するため、開始後ならいつでも受け付ける
                if (s.Status == RunStatus.NotStarted) throw new CommandRejectedException("ws_action: no task");
                var type = cmd.Get("op") switch
                {
                    "minimize" => Ev.WsMinimize, "restore" => Ev.WsRestore, "close" => Ev.WsClose,
                    var x => throw new CommandRejectedException($"ws_action: bad op: {x}"),
                };
                E(type, ("hwnd", cmd.Get("hwnd")), ("process", cmd.Get("process")), ("result", cmd.Get("result")), ("phase", cmd.Get("phase")));
                break;
            }

            case Cmd.Focus:
            {
                RequireRunning();
                var process = cmd.Get("process");
                var title = cmd.Get("title");
                var action = _opt.Exclusions.ForWindow(process, title);
                if (action == ExclusionAction.Ignore) return; // 記録しない(ログにも残さない)
                if (action == ExclusionAction.Mask) title = "[masked]";
                bool tw; string reason;
                if (cmd.Has("task_window")) { tw = CoreEvent.Truthy(cmd.Get("task_window")); reason = "explicit"; }
                else (tw, reason) = TaskWindowClassifier.Classify(s, process, title, cmd.Get("hwnd"), _opt.TaskApps, _opt.InterruptApps);
                E(Ev.FocusChanged, ("process", process), ("title", title), ("hwnd", cmd.Get("hwnd")),
                    ("task_window", tw ? "yes" : "no"), ("tw_reason", reason));
                break;
            }

            case Cmd.IdleStart:
                RequireRunning();
                if (cmd.Has("last_input_at")) E(Ev.IdleStarted, ("last_input_at", cmd.Get("last_input_at")));
                else E(Ev.IdleStarted);
                break;

            case Cmd.IdleEnd:
                RequireRunning();
                E(Ev.IdleEnded);
                break;

            case Cmd.Resource:
            {
                RequireRunning();
                if (s.IsPaused) return; // 一時停止中はアクセス記録を止める(F-15)
                var kind = cmd.Get("kind");
                if (kind is not ("folder" or "file" or "url")) throw new CommandRejectedException($"resource: bad kind: {kind}");
                var loc = cmd.Get("location");
                if (_opt.Exclusions.ForResource(kind, loc) == ExclusionAction.Ignore) return;
                var d = new List<(string, string)> { ("kind", kind), ("location", loc), ("source", cmd.Get("source", "manual")) };
                if (cmd.Has("name")) d.Add(("name", cmd.Get("name")));
                emit(Ev.ResourceSeen, d.ToArray());
                break;
            }

            case Cmd.WindowOpened:
                RequireRunning();
                E(Ev.WindowOpened, ("hwnd", cmd.Get("hwnd")), ("process", cmd.Get("process")), ("title", cmd.Get("title")));
                break;

            case Cmd.WindowClosed:
                RequireRunning();
                E(Ev.WindowClosed, ("hwnd", cmd.Get("hwnd")));
                break;

            case Cmd.Note:
                RequireRunning();
                E(Ev.NoteAdded, ("text", cmd.Get("text")));
                break;

            case Cmd.TrayAdd:
                RequireRunning();
                E(Ev.TrayItemAdded, ("kind", cmd.Get("kind", "file")), ("ref", cmd.Get("ref")), ("note", cmd.Get("note")));
                break;

            case Cmd.Set:
            {
                var name = cmd.Get("name");
                var value = cmd.Get("value");
                if (!s.Params.Clone().TrySet(name, value, out var old, out var err)) throw new CommandRejectedException("set: " + err);
                E(Ev.ParamSet, ("name", name), ("old", old.ToString("R", CultureInfo.InvariantCulture)), ("new", value));
                break;
            }

            case Cmd.EndTask:
                RequireOpen(); // 中断中(suspended)の実行も、そのまま終了できる
                if (s.ActiveIndex >= 0) E(Ev.SubtaskCompleted, ("index", s.ActiveIndex.ToString()));
                E(Ev.TaskEnded);
                break;

            case Cmd.AbortTask:
                RequireOpen();
                E(Ev.TaskAborted, ("reason", cmd.Get("reason")));
                break;

            default:
                throw new CommandRejectedException($"unknown command: {cmd.Type}");
        }
    }

    private int NextPendingAfter(int cur)
    {
        for (var i = cur + 1; i < State.Subtasks.Count; i++)
            if (State.Subtasks[i].Status == SubtaskStatus.Pending) return i;
        return -1;
    }
}
