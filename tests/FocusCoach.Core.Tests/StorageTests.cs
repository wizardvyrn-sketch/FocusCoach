using FocusCoach.Core.Scenarios;
using FocusCoach.Storage;
using Xunit;

namespace FocusCoach.Core.Tests;

public class StorageTests
{
    [Fact]
    public void Jsonl_line_roundtrip_keeps_japanese_and_format()
    {
        var e = new CoreEvent(12, new DateTimeOffset(2026, 10, 3, 10, 15, 2, 123, TimeSpan.FromHours(9)), "focus_changed",
            new Dictionary<string, string> { ["title"] = "見積書.xlsx - Excel", ["task_window"] = "yes" });
        var line = JsonlFormat.ToLine(e);
        Assert.Contains("\"seq\":12", line);
        Assert.Contains("見積書.xlsx", line); // エスケープされず読める
        Assert.DoesNotContain('\n', line);
        var back = JsonlFormat.FromLine(line);
        Assert.Equal(e.Seq, back.Seq);
        Assert.Equal(e.Ts, back.Ts);
        Assert.Equal("見積書.xlsx - Excel", back.Get("title"));
    }

    [Fact]
    public void Reader_drops_truncated_last_line_only()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fc_test_{Guid.NewGuid():N}.jsonl");
        try
        {
            File.WriteAllLines(path, [
                JsonlFormat.ToLine(new CoreEvent(1, DateTimeOffset.UnixEpoch, "a", new Dictionary<string, string>())),
                JsonlFormat.ToLine(new CoreEvent(2, DateTimeOffset.UnixEpoch, "b", new Dictionary<string, string>())),
                "{\"seq\":3,\"ts\":\"2026-01-01T00:0"]);
            Assert.Equal(2, JsonlEventReader.Read(path).Count);
            File.WriteAllLines(path, ["{broken", JsonlFormat.ToLine(new CoreEvent(2, DateTimeOffset.UnixEpoch, "b", new Dictionary<string, string>()))]);
            Assert.ThrowsAny<Exception>(() => JsonlEventReader.Read(path)); // 途中の破損は握りつぶさない
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Writer_flushes_every_event_so_file_is_readable_before_dispose()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fc_test_{Guid.NewGuid():N}.jsonl");
        using var w = new JsonlEventWriter(path);
        w.Append(new CoreEvent(1, DateTimeOffset.UnixEpoch, "a", new Dictionary<string, string>()));
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var rd = new StreamReader(fs))
            Assert.Contains("\"type\":\"a\"", rd.ReadToEnd());
        // Windows では読み取りハンドルを閉じてからでないと削除できない
        w.Dispose();
        File.Delete(path);
    }

    [Fact]
    public void Db_saves_run_and_serves_history_for_guide()
    {
        using var db = new FocusCoachDb(":memory:");
        Assert.Equal(FocusCoachDb.SchemaVersion, db.UserVersion);
        var r = ScenarioRunner.Run(File.ReadAllText(Path.Combine(TestPaths.Scenarios, "basic_two_subtasks.txt")));
        db.SaveRun(r.Session.State, r.Metrics);
        db.SaveRun(r.Session.State, r.Metrics); // 同じ run_id は置き換え(重複しない)
        Assert.Equal(1, db.CountRuns());
        Assert.Equal(1, db.CountResources("scenario"));

        var hist = db.LoadHistory("開発計画書作成");
        var h = Assert.Single(hist);
        Assert.Equal(2400, h.TotalSec);
        Assert.Equal(2, h.Subtasks.Count);
        var guide = Guide.Build(hist, new ParamRegistry());
        Assert.Equal(1, guide.SampleCount);
        Assert.Equal("低", guide.Confidence);
        Assert.Empty(db.LoadHistory("存在しない種別"));
    }

    [Fact]
    public void Db_excludes_running_and_aborted_runs_from_history()
    {
        using var db = new FocusCoachDb(":memory:");
        var r = ScenarioRunner.Run("at 00:00 start_task category=X\nat 00:10 abort_task");
        db.SaveRun(r.Session.State, r.Metrics);
        Assert.Empty(db.LoadHistory("X"));
    }

    [Fact]
    public void Db_file_is_reopened_without_remigration()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fc_test_{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new FocusCoachDb(path)) db.EnsureCategory("A");
            using (var db2 = new FocusCoachDb(path)) { Assert.Equal(FocusCoachDb.SchemaVersion, db2.UserVersion); Assert.Equal(1, db2.EnsureCategory("A")); }
        }
        finally { foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) try { File.Delete(f); } catch { } }
    }
}

public class AppFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fc_app_{Guid.NewGuid():N}");
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void Settings_roundtrip_and_broken_file_falls_back_to_defaults()
    {
        var s = AppSettings.Load(_root);
        Assert.Equal(0x78, s.HotkeyInterrupt);
        Assert.True(s.WorkspaceDryRun);
        s.PanelLeft = 100; s.HotkeyMemo = 0x7A;
        s.Save(_root);
        var t = AppSettings.Load(_root);
        Assert.Equal(100, t.PanelLeft);
        Assert.Equal(0x7A, t.HotkeyMemo);
        File.WriteAllText(AppSettings.PathFor(_root), "{ broken");
        Assert.Equal(0x78, AppSettings.Load(_root).HotkeyInterrupt);
    }

    [Fact]
    public void AppLog_writes_daily_file_and_crash_log()
    {
        var log = new AppLog(_root);
        log.Info("Core", "started " + LogMask.Mask("title", "顧客A"));
        log.Error("UI", "boom", new InvalidOperationException("x"));
        var file = Assert.Single(Directory.GetFiles(Path.Combine(_root, "logs"), "app-*.log"));
        var text = File.ReadAllText(file);
        Assert.Contains("[INF] Core: started title:", text);
        Assert.DoesNotContain("顧客A", text);
        Assert.Contains("InvalidOperationException", text);
        var crash = log.WriteCrash(new Exception("crash"), "status=Running subtask=1/3");
        Assert.Contains("status=Running", File.ReadAllText(crash));
    }
}

public class StepNotesStorageTests
{
    [Fact]
    public void First_step_of_previous_run_is_offered_and_next_step_is_saved()
    {
        using var db = new FocusCoachDb(":memory:");
        Assert.Null(db.LoadLastFirstStep("開発計画書作成"));
        var r1 = ScenarioRunner.Run("""
            at 00:00 start_task category="開発計画書作成" first_step="見積書を開く"
            at 00:05 interrupt
            at 00:06 interrupt_note next="合計を照合"
            at 00:10 resume
            at 00:20 end_task
            """, runId: "a");
        db.SaveRun(r1.Session.State, r1.Metrics);
        var r2 = ScenarioRunner.Run("at 00:00 start_task category=\"開発計画書作成\"\nat 00:10 end_task", runId: "b");
        db.SaveRun(r2.Session.State, r2.Metrics); // 新しい実行で最初の一手を書かなかった場合は、さらに前のものを使う
        Assert.Equal("見積書を開く", db.LoadLastFirstStep("開発計画書作成"));
        Assert.Null(db.LoadLastFirstStep("別の種別"));
    }
}
