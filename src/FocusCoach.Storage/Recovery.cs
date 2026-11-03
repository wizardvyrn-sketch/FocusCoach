using FocusCoach.Core;

namespace FocusCoach.Storage;

/// <summary>再開の候補。Kind: interrupted(異常終了)/ suspended(持ち越しで中断中)。</summary>
public sealed record RecoverableRun(
    string RunId, string Path, string Kind, string Category, string Project,
    DateTimeOffset? StartedAt, DateTimeOffset LastEventAt, string? ActiveSubtask);

/// <summary>
/// 起動時に sessions/*.jsonl を調べ、終了していない実行を探す(F-16、F-15 の持ち越し)。
/// JSONL を正とし、DB は補助とする。
/// </summary>
public static class RecoveryScanner
{
    public static List<RecoverableRun> Find(string dataRoot)
    {
        var dir = System.IO.Path.Combine(dataRoot, "sessions");
        var res = new List<RecoverableRun>();
        if (!Directory.Exists(dir)) return res;
        foreach (var path in Directory.GetFiles(dir, "*.jsonl"))
        {
            List<CoreEvent> events;
            try { events = JsonlEventReader.Read(path); }
            catch (Exception) { continue; } // 途中が壊れたファイルは候補にしない([仮案-25] 修復ツールは未実装)
            if (events.Count == 0) continue;
            var s = Reducer.Replay(events);
            var kind = s.Status switch
            {
                RunStatus.Running => "interrupted",
                RunStatus.Suspended => "suspended",
                _ => null,
            };
            if (kind is null) continue;
            var active = s.Status == RunStatus.Suspended ? s.SuspendedActiveIndex : s.ActiveIndex;
            res.Add(new RecoverableRun(s.RunId, path, kind, s.Category, s.Project, s.StartedAt, s.LastEventAt,
                active >= 0 && active < s.Subtasks.Count ? s.Subtasks[active].Name : null));
        }
        return res.OrderByDescending(r => r.LastEventAt).ToList();
    }
}
