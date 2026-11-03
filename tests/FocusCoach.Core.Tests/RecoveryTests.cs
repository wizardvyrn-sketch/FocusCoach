using FocusCoach.Core.Scenarios;
using FocusCoach.Core.Workspace;
using FocusCoach.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FocusCoach.Core.Tests;

public class RecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fc_rec_{Guid.NewGuid():N}");
    private static readonly DateTimeOffset T0 = ScenarioRunner.Base;

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static CoreCommand C(int min, string type, params (string, string)[] d) =>
        new(type, d.ToDictionary(x => x.Item1, x => x.Item2), T0.AddMinutes(min));

    private string StartRun(string runId, Action<Session> work)
    {
        var path = JsonlEventWriter.PathFor(_root, runId);
        using var w = new JsonlEventWriter(path);
        var s = new Session(runId, new ManualClock(T0), w);
        work(s);
        return path;
    }

    [Fact]
    public void Crash_with_truncated_line_then_restore_continues_same_file_and_counts_gap_as_pause()
    {
        var path = StartRun("r1", s =>
        {
            s.Dispatch(C(0, Cmd.StartTask, ("category", "x"), ("subtasks", "A,B")));
            s.Dispatch(C(20, Cmd.NextSubtask));
        });
        File.AppendAllText(path, "{\"seq\":99,\"ts\":\"2026-01-0"); // 書きかけで落ちた

        var found = RecoveryScanner.Find(_root);
        var r = Assert.Single(found);
        Assert.Equal("interrupted", r.Kind);
        Assert.Equal("B", r.ActiveSubtask);

        var events = JsonlEventReader.Read(path);
        using (var w = new JsonlEventWriter(path)) // 末尾を修復してから追記
        {
            var s = Session.Restore(events, new ManualClock(T0), w);
            s.Dispatch(C(50, Cmd.ResumeTask, ("reason", "crash")));   // 30分後に再起動して再開
            s.Dispatch(C(60, Cmd.EndTask));
        }

        var all = JsonlEventReader.Read(path); // 途中が壊れていれば例外になる
        Assert.Equal(Enumerable.Range(1, all.Count).Select(i => (long)i), all.Select(e => e.Seq));
        var st = Reducer.Replay(all);
        var m = MetricsCalculator.Compute(st, st.EndedAt!.Value);
        Assert.Equal(RunStatus.Completed, st.Status);
        Assert.Equal(30 * 60, m.PausedSec, 3);
        Assert.Contains(st.Pauses, p => p.Reason == "crash");
        // B: 20〜60分の40分から、最後のイベント(20分)〜再開(50分)の空白30分を除いた10分
        Assert.Equal(10 * 60, m.Subtasks[1].DurationSec, 3);
        Assert.Empty(RecoveryScanner.Find(_root));
    }

    [Fact]
    public void Suspended_run_is_listed_and_resumed_next_day_as_the_same_run()
    {
        var path = StartRun("r2", s =>
        {
            s.Dispatch(C(0, Cmd.StartTask, ("category", "x"), ("subtasks", "A")));
            s.Dispatch(C(40, Cmd.SuspendTask));
        });
        var r = Assert.Single(RecoveryScanner.Find(_root));
        Assert.Equal("suspended", r.Kind);
        Assert.Equal("A", r.ActiveSubtask);

        var restored = Session.Restore(JsonlEventReader.Read(path), new ManualClock(T0));
        Assert.Equal(RunStatus.Suspended, restored.State.Status);
        Assert.Throws<CommandRejectedException>(() => restored.Dispatch(C(24 * 60, Cmd.Note, ("text", "中断中は不可"))));
        var evs = restored.Dispatch(C(24 * 60, Cmd.ResumeTask, ("reason", "crash"))); // 中断中なら理由に関係なく翌日再開として扱う
        Assert.Equal("next_day", evs.Single(e => e.Type == Ev.SessionResumed).Get("reason"));
        Assert.Equal(RunStatus.Running, restored.State.Status);
        Assert.Equal(0, restored.State.ActiveIndex);
    }

    [Fact]
    public void Suspend_resume_roundtrip_through_files()
    {
        var path = StartRun("r3", s =>
        {
            s.Dispatch(C(0, Cmd.StartTask, ("category", "x"), ("subtasks", "A")));
            s.Dispatch(C(40, Cmd.SuspendTask));
        });
        using (var w = new JsonlEventWriter(path))
        {
            var s = Session.Restore(JsonlEventReader.Read(path), new ManualClock(T0), w);
            s.Dispatch(C(24 * 60, Cmd.ResumeTask));
            s.Dispatch(C(24 * 60 + 20, Cmd.EndTask));
        }
        var st = Reducer.Replay(JsonlEventReader.Read(path));
        var m = MetricsCalculator.Compute(st, st.EndedAt!.Value);
        Assert.Equal(60 * 60, m.ActiveSec, 3);
        Assert.Equal(2, m.WorkDays);
        Assert.False(m.SameDay);
        Assert.Equal(1, st.ResumeCount);
    }

    [Fact]
    public void Scanner_ignores_completed_and_broken_runs()
    {
        StartRun("done", s => { s.Dispatch(C(0, Cmd.StartTask, ("category", "x"))); s.Dispatch(C(1, Cmd.EndTask)); });
        Directory.CreateDirectory(Path.Combine(_root, "sessions"));
        File.WriteAllText(Path.Combine(_root, "sessions", "broken.jsonl"), "{broken\n{\"seq\":1}\n");
        Assert.Empty(RecoveryScanner.Find(_root));
        Assert.Empty(RecoveryScanner.Find(Path.Combine(_root, "nothing")));
    }

    [Fact]
    public void RepairTail_cuts_only_an_unterminated_last_line()
    {
        var p = Path.Combine(Path.GetTempPath(), $"fc_tail_{Guid.NewGuid():N}.jsonl");
        try
        {
            File.WriteAllText(p, "a\nb\n");
            Assert.Equal(0, JsonlEventWriter.RepairTail(p));
            File.WriteAllText(p, "a\nb\nccc");
            Assert.Equal(3, JsonlEventWriter.RepairTail(p));
            Assert.Equal("a\nb\n", File.ReadAllText(p));
            File.WriteAllText(p, "only-partial");
            JsonlEventWriter.RepairTail(p);
            Assert.Equal("", File.ReadAllText(p));
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void Restore_rejects_empty_and_continues_seq()
    {
        Assert.Throws<ArgumentException>(() => Session.Restore([], new ManualClock(T0)));
        var s1 = new Session("r", new ManualClock(T0));
        var evs = s1.Dispatch(C(0, Cmd.StartTask, ("category", "x"))).ToList();
        var s2 = Session.Restore(evs, new ManualClock(T0));
        var next = s2.Dispatch(C(5, Cmd.Note, ("text", "n")));
        Assert.Equal(evs.Max(e => e.Seq) + 1, next[0].Seq);
        Assert.Equal("r", s2.RunId);
        Assert.Throws<CommandRejectedException>(() => s2.Dispatch(C(6, Cmd.ResumeTask))); // 実行中で reason 無しは再開の対象外
    }

    [Fact]
    public void Db_header_row_tracks_status_and_interrupted_runs_are_not_history()
    {
        using var db = new FocusCoachDb(":memory:");
        var s = new Session("h1", new ManualClock(T0));
        s.Dispatch(C(0, Cmd.StartTask, ("category", "x")));
        db.UpsertRunHeader(s.State);
        Assert.Equal("running", db.GetStatus("h1"));
        s.Dispatch(C(10, Cmd.SuspendTask));
        db.UpsertRunHeader(s.State);
        Assert.Equal("suspended", db.GetStatus("h1"));
        db.MarkInterrupted("h1");
        Assert.Equal("interrupted", db.GetStatus("h1"));
        Assert.Empty(db.LoadHistory("x"));

        s.Dispatch(C(24 * 60, Cmd.ResumeTask));
        s.Dispatch(C(24 * 60 + 5, Cmd.EndTask));
        db.SaveRun(s.State, MetricsCalculator.Compute(s.State, s.State.EndedAt!.Value));
        Assert.Equal("completed", db.GetStatus("h1"));
        Assert.Single(db.LoadHistory("x"));
    }

    [Fact]
    public void Db_migrates_from_v1_to_latest()
    {
        var path = Path.Combine(_root, "old.db");
        using (var db = new FocusCoachDb(path)) { }
        using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "ALTER TABLE run_metrics DROP COLUMN work_days; ALTER TABLE run_metrics DROP COLUMN same_day; " +
                "ALTER TABLE task_runs DROP COLUMN first_step; ALTER TABLE subtask_runs DROP COLUMN next_step; " +
                "ALTER TABLE subtask_templates DROP COLUMN target_min; " +
                "ALTER TABLE task_categories DROP COLUMN draft_project; ALTER TABLE task_categories DROP COLUMN draft_steps; ALTER TABLE task_categories DROP COLUMN draft_task_apps; " +
                "ALTER TABLE task_categories DROP COLUMN draft_first_step; ALTER TABLE task_categories DROP COLUMN last_used_at; PRAGMA user_version=1;";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        using var db2 = new FocusCoachDb(path);
        Assert.Equal(FocusCoachDb.SchemaVersion, db2.UserVersion);
        var r = ScenarioRunner.Run(File.ReadAllText(Path.Combine(TestPaths.Scenarios, "basic_two_subtasks.txt")));
        db2.SaveRun(r.Session.State, r.Metrics); // 新しい列に書ける
        Assert.Single(db2.LoadHistory("開発計画書作成"));
    }

    [Fact]
    public void Window_restore_after_crash_only_on_same_boot_and_only_minimized_snapshot_windows()
    {
        var snap = new[] { new WindowInfo("1", "WINWORD.EXE", "a - Word", Placement: "AA"), new WindowInfo("2", "chrome.exe", "b"), new WindowInfo("3", "notepad.exe", "c", Minimized: true) };
        var sink = new ListSink();
        var s = new Session("w", new ManualClock(T0), sink);
        s.Dispatch(C(0, Cmd.StartTask, ("category", "x")));
        s.Dispatch(C(0, Cmd.WsSnapshot, ("phase", "start"), ("boot_id", "BOOT-1"), ("windows", RecoveryPlanner.Serialize(snap))));
        var current = new[]
        {
            snap[0] with { Minimized = true, Placement = null },   // FocusCoach が最小化した → 戻す
            snap[1],                                                // 利用者がもう戻した → 触らない
            snap[2],                                                // 元から最小化 → 触らない
            new WindowInfo("9", "excel.exe", "new", Minimized: true), // スナップショットに無い → 触らない
        };
        var same = RecoveryPlanner.PlanRestoreAfterCrash(s.State, "BOOT-1", current);
        var step = Assert.Single(same);
        Assert.Equal("1", step.Window.Hwnd);
        Assert.Equal("AA", step.Window.Placement);
        Assert.Empty(RecoveryPlanner.PlanRestoreAfterCrash(s.State, "BOOT-2", current)); // 再起動後は何もしない

        // JSONL から復元しても、スナップショットが残っている(異常終了後に使うため)
        var replayed = Reducer.Replay(sink.Events);
        Assert.Equal("BOOT-1", replayed.WsSnapshots["start"].BootId);
        Assert.Single(RecoveryPlanner.PlanRestoreAfterCrash(replayed, "BOOT-1", current));
    }
}
