namespace FocusCoach.Core;

public enum RunStatus { NotStarted, Running, Suspended, Completed, Aborted }
public enum SubtaskStatus { Pending, Active, Done, Skipped }

public sealed class TimeSpanRec
{
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset? End { get; set; }
}

public sealed class SubtaskState
{
    public int Index { get; init; }
    public string Name { get; set; } = "";
    public double? TargetSec { get; set; }
    public SubtaskStatus Status { get; set; } = SubtaskStatus.Pending;
    public List<TimeSpanRec> Intervals { get; } = [];

    /// <summary>割り込み退避時の「どこまでやったか」(09章 §9.4.1)。</summary>
    public string? ResumeNote { get; set; }

    /// <summary>再開したら最初にやること(再開メモの2行目。09章 §9.4.1、I-01)。</summary>
    public string? NextStep { get; set; }
}

public sealed class PauseRec
{
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset? End { get; set; }
    public string Reason { get; set; } = "other";
}

public sealed class ForegroundSeg
{
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset? End { get; set; }
    public string Process { get; set; } = "";
    public string Title { get; set; } = "";
    public string Hwnd { get; set; } = "";
    public bool TaskWindow { get; set; }
}

public sealed class OpenedWindow
{
    public string Hwnd { get; init; } = "";
    public string Process { get; init; } = "";
    public string Title { get; init; } = "";
    public DateTimeOffset OpenedAt { get; init; }
    public DateTimeOffset? ClosedAt { get; set; }
}

public sealed class ResourceRec
{
    public string Kind { get; init; } = "";
    public string Location { get; init; } = "";
    public string DisplayName { get; set; } = "";
    public string Source { get; set; } = "";
    public DateTimeOffset FirstSeen { get; init; }
    public DateTimeOffset LastSeen { get; set; }
    /// <summary>最初に見たときにアクティブだったサブタスク(なければ null)。</summary>
    public int? SubtaskIndex { get; init; }
}

public sealed record NoteRec(DateTimeOffset At, int? SubtaskIndex, string Text);
public sealed record InterruptRec(DateTimeOffset At, string Kind, string Note, int? SubtaskIndex);
/// <summary>出口カードで閉じた割り込み(design-0.12A §3)。Followup は「あとでやること」(無ければ空)。</summary>
public sealed record InterruptCloseRec(DateTimeOffset At, string Category, string Title, string Outcome, string Followup, string Due,
    double InterruptSec, string Via, int GuideSec);
/// <summary>再入場ガイドの終わり(design-0.12A §4.4)。</summary>
public sealed record ReentryRec(DateTimeOffset At, string How, double ShownSec, int Extends);
public sealed record TrayItem(DateTimeOffset At, int? SubtaskIndex, string Kind, string Ref, string Note);

/// <summary>
/// イベント列を畳み込んだ状態(08章 §8.1-1)。Reducer 以外から変更しない。
/// Core の状態は「初期状態 + イベント列」だけで決まる。
/// </summary>
public sealed class RunState
{
    public string RunId { get; set; } = "";
    public ParamRegistry Params { get; } = new();
    public RunStatus Status { get; set; } = RunStatus.NotStarted;
    public string Category { get; set; } = "";
    public string Project { get; set; } = "";
    /// <summary>開始時に書いた「最初の一手」(05 F-02、I-05)。</summary>
    public string FirstStep { get; set; } = "";
    public double? TargetSec { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public DateTimeOffset LastEventAt { get; set; }
    public long EventCount { get; set; }

    public List<SubtaskState> Subtasks { get; } = [];
    public int ActiveIndex { get; set; } = -1;
    public SubtaskState? Active => ActiveIndex >= 0 ? Subtasks[ActiveIndex] : null;

    public List<PauseRec> Pauses { get; } = [];
    /// <summary>開いている一時停止。後から挿入された区間(分類し直し・異常終了の空白)があるので、末尾とは限らない。</summary>
    public PauseRec? OpenPause => Pauses.LastOrDefault(p => p.End is null);
    public bool IsPaused => OpenPause is not null;

    public List<ForegroundSeg> Foreground { get; } = [];
    public ForegroundSeg? CurrentForeground => Foreground.Count > 0 && Foreground[^1].End is null ? Foreground[^1] : null;

    /// <summary>確定したアイドル区間(しきい値以上のもの)。</summary>
    public List<TimeSpanRec> IdleSpans { get; } = [];
    public DateTimeOffset? OpenIdleFrom { get; set; }

    public List<OpenedWindow> OpenedDuring { get; } = [];
    public Dictionary<string, ResourceRec> Resources { get; } = [];
    public List<NoteRec> Notes { get; } = [];
    public List<InterruptRec> Interrupts { get; } = [];
    public List<string> Deferred { get; } = [];
    public List<TrayItem> Tray { get; } = [];
    public List<InterruptCloseRec> InterruptCloses { get; } = [];
    public List<ReentryRec> Reentries { get; } = [];

    /// <summary>開始準備で選んだ「タスクで使うアプリ」(正規化済みのプロセス名。05 F-02)。</summary>
    public List<string> TaskApps { get; } = [];

    /// <summary>中断(suspended)・異常終了からの再開の回数。</summary>
    public int ResumeCount { get; set; }

    /// <summary>最新のワークスペースのスナップショット(phase → boot_id と windows の JSON)。</summary>
    public Dictionary<string, (string BootId, string WindowsJson)> WsSnapshots { get; } = [];

    /// <summary>中断した時点でアクティブだったサブタスク(再開時に戻す)。</summary>
    public int SuspendedActiveIndex { get; set; } = -1;
}
