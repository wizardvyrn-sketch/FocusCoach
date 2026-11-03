using System.Globalization;

namespace FocusCoach.Core;

/// <summary>
/// セッションイベント(JSONL の1行 = 1イベント。docs/spec/04 §4.3, 08 §8.3)。
/// [仮案-01] data は string→string の平坦な辞書にした(JSONの入れ子を避けて単純化)。必要なら型付きに拡張する。
/// </summary>
public sealed record CoreEvent(long Seq, DateTimeOffset Ts, string Type, IReadOnlyDictionary<string, string> Data)
{
    public string Get(string key, string def = "") => Data.TryGetValue(key, out var v) ? v : def;

    public bool Has(string key) => Data.ContainsKey(key);

    public double GetNum(string key, double def = 0) =>
        Data.TryGetValue(key, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : def;

    public bool GetBool(string key, bool def = false) =>
        Data.TryGetValue(key, out var v) ? Truthy(v) : def;

    public DateTimeOffset GetTime(string key, DateTimeOffset def) =>
        Data.TryGetValue(key, out var v) && DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : def;

    public static bool Truthy(string v) => v.Trim().ToLowerInvariant() is "yes" or "true" or "1" or "on";
}

/// <summary>
/// Core への唯一の入力経路(08章 §8.1-3)。UIボタン・ホットキー・CLIシナリオ・OS監視のいずれもこれを送る。
/// At が null のときは IClock の現在時刻を使う。
/// </summary>
public sealed record CoreCommand(string Type, IReadOnlyDictionary<string, string> Data, DateTimeOffset? At = null)
{
    public static CoreCommand Of(string type, params (string Key, string Value)[] data) =>
        new(type, data.ToDictionary(x => x.Key, x => x.Value));

    public static CoreCommand At_(DateTimeOffset at, string type, params (string Key, string Value)[] data) =>
        new(type, data.ToDictionary(x => x.Key, x => x.Value), at);

    public string Get(string key, string def = "") => Data.TryGetValue(key, out var v) ? v : def;
    public bool Has(string key) => Data.ContainsKey(key);
}

public static class Cmd
{
    public const string StartTask = "start_task";
    public const string EndTask = "end_task";
    public const string AbortTask = "abort_task";
    public const string NextSubtask = "next_subtask";
    public const string SkipSubtask = "skip_subtask";
    public const string GotoSubtask = "goto_subtask";
    public const string AddSubtask = "add_subtask";
    public const string Pause = "pause";
    public const string Resume = "resume";
    public const string Interrupt = "interrupt";
    public const string Focus = "focus";
    public const string IdleStart = "idle_start";
    public const string IdleEnd = "idle_end";
    public const string Resource = "resource";
    public const string WindowOpened = "window_opened";
    public const string WindowClosed = "window_closed";
    public const string Note = "note";
    public const string TrayAdd = "tray_add";
    public const string Set = "set";
    /// <summary>直前の割り込み(なければ現在)のサブタスクに「どこまでやったか」を付ける(09章 §9.4.1、F10)。</summary>
    public const string InterruptNote = "interrupt_note";
    /// <summary>OS監視からの離席の開始/終了(reason: lock / sleep / remote)。自動の一時停止(F-15)。</summary>
    public const string AwayStart = "away_start";
    public const string AwayEnd = "away_end";
    /// <summary>直前のアイドル区間、または自動の一時停止を、割り込みに分類し直す(09章 §9.4.1a)。target: idle / pause</summary>
    public const string AwayReclassify = "away_reclassify";
    /// <summary>中断して後日再開(持ち越し。F-15)。</summary>
    public const string SuspendTask = "suspend_task";
    /// <summary>中断中、または異常終了した実行を再開する(reason: next_day / crash)。</summary>
    public const string ResumeTask = "resume_task";
    /// <summary>ワークスペースのスナップショットを記録する(phase: start / interrupt、boot_id、windows=JSON)。異常終了からの復元に使う(F-16)。</summary>
    public const string WsSnapshot = "ws_snapshot";
    /// <summary>ウィンドウ操作の結果を記録する(op: minimize / restore / close、hwnd、result)。</summary>
    public const string WsAction = "ws_action";
    /// <summary>
    /// 割り込みを閉じる(出口カード S-11a。design-0.12A §3)。category、title、outcome(done / followup)、followup(あとでやること)、
    /// due(yyyy-MM-dd)、tidy(yes/no)、guide_sec(再入場ガイドの秒数。0 は出さない)、via(f9 / away)。
    /// via=f9 は開いている割り込みの一時停止を終える。via=away は、離席を割り込みに分類し直した直後の記録だけ。
    /// </summary>
    public const string InterruptClose = "interrupt_close";
    /// <summary>再入場ガイド(S-11b)の終わり。how: timeout / now / interrupt / closed、shown_sec、extends。</summary>
    public const string ReentryEnd = "reentry_end";
}

public static class Ev
{
    public const string SessionStart = "session_start";
    public const string TaskStarted = "task_started";
    public const string TaskEnded = "task_ended";
    public const string TaskAborted = "task_aborted";
    public const string SubtaskAdded = "subtask_added";
    public const string SubtaskStarted = "subtask_started";
    public const string SubtaskCompleted = "subtask_completed";
    public const string SubtaskSkipped = "subtask_skipped";
    public const string Paused = "paused";
    public const string Resumed = "resumed";
    public const string FocusChanged = "focus_changed";
    public const string IdleStarted = "idle_started";
    public const string IdleEnded = "idle_ended";
    public const string WindowOpened = "window_opened";
    public const string WindowClosed = "window_closed";
    public const string ResourceSeen = "resource_seen";
    public const string NoteAdded = "note_added";
    public const string InterruptMarked = "interrupt_marked";
    public const string InterruptDeferred = "interrupt_deferred";
    public const string ResumeCardShown = "resume_card_shown";
    public const string TrayItemAdded = "tray_item_added";
    public const string ParamSet = "param_set";
    public const string InterruptNoteAdded = "interrupt_note_added";
    public const string AwayReclassified = "away_reclassified";
    public const string TaskSuspended = "task_suspended";
    public const string SessionResumed = "session_resumed";
    public const string WsSnapshot = "ws_snapshot";
    public const string WsMinimize = "ws_minimize";
    public const string WsRestore = "ws_restore";
    public const string WsClose = "ws_close";
    public const string InterruptClosed = "interrupt_closed";
    public const string ReentryEnded = "reentry_ended";
}

public interface IEventSink
{
    void Append(CoreEvent e);
}

public sealed class CommandRejectedException(string message) : Exception(message);
