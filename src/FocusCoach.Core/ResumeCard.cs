namespace FocusCoach.Core;

public sealed record ResumeCardData(
    string SubtaskName, double PausedSec, string? ResumeNote, string? NextStep,
    IReadOnlyList<string> Resources, IReadOnlyList<TrayItem> Tray, int DeferredCount);

/// <summary>
/// 復帰カードの中身(09章 §9.4.2)。文面は事実の提示だけで、作業の順序は指示しない。
/// 「次の一手」は利用者自身が書いたもの(I-01)。
/// </summary>
public static class ResumeCard
{
    public static ResumeCardData Build(RunState s, DateTimeOffset now)
    {
        // 対象は直前の「すぐ対応」の割り込みのサブタスク(再開後に次のサブタスクへ進んでいても、メモはそこに付いている)。無ければ現在のサブタスク
        var idx = s.Interrupts.LastOrDefault(i => i.Kind == "now")?.SubtaskIndex ?? (s.ActiveIndex >= 0 ? s.ActiveIndex : (int?)null);
        var sub = idx is { } i && i < s.Subtasks.Count ? s.Subtasks[i] : null;
        var pausedSec = s.OpenPause is { } p ? (now - p.Start).TotalSeconds
            : s.Pauses.LastOrDefault(x => x.Reason == "interrupt" && x.End is not null) is { } last ? (last.End!.Value - last.Start).TotalSeconds : 0;
        return new ResumeCardData(
            sub?.Name ?? "",
            pausedSec,
            sub?.ResumeNote,
            sub?.NextStep,
            s.Resources.Values.Where(r => r.SubtaskIndex == idx).OrderBy(r => r.FirstSeen).Select(r => r.DisplayName).ToList(),
            s.Tray.Where(t => t.SubtaskIndex == idx).ToList(),
            s.Deferred.Count);
    }
}
