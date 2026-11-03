using FocusCoach.Core.Scenarios;
using FocusCoach.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FocusCoach.Core.Tests;

/// <summary>手順エディタとタスクのプルダウン(05 F-01・F-02、03 S-02 / S-02a)。</summary>
public class StepListTests
{
    [Fact]
    public void Format_and_parse_roundtrip_with_and_without_target()
    {
        var steps = new List<StepItem> { new("見積確認", 25), new("概略仕様確認", null), new("執筆", 12.5) };
        var s = StepList.Format(steps);
        Assert.Equal("見積確認:25,概略仕様確認,執筆:12.5", s);
        Assert.Equal(steps, StepList.Parse(s));
        Assert.Equal("見積確認 25分  →  概略仕様確認  →  執筆 12.5分", StepList.Summary(steps));
        Assert.Equal("(手順なし)", StepList.Summary([]));
    }

    [Fact]
    public void Normalize_trims_drops_blank_rows_and_reports_problems()
    {
        var (ok, e1) = StepList.Normalize([(" 見積確認 ", "25"), ("", ""), ("執筆", "")]);
        Assert.Empty(e1);
        Assert.Equal([new StepItem("見積確認", 25), new StepItem("執筆", null)], ok);

        var (_, e2) = StepList.Normalize([("A", "0"), ("a", "5"), ("B,C", ""), ("", "10"), ("D", "x"), ("E", "601")]);
        Assert.Contains(e2, x => x.StartsWith("1行目") && x.Contains("目安"));
        Assert.Contains(e2, x => x.StartsWith("2行目") && x.Contains("重複"));   // 大文字小文字は区別しない
        Assert.Contains(e2, x => x.StartsWith("3行目") && x.Contains("カンマ"));
        Assert.Contains(e2, x => x.StartsWith("4行目") && x.Contains("名前"));
        Assert.Contains(e2, x => x.StartsWith("5行目"));
        Assert.Contains(e2, x => x.StartsWith("6行目"));

        var (_, e3) = StepList.Normalize([("", "")]);
        Assert.Equal(["手順を1つ以上入れてください"], e3);
    }

    [Fact]
    public void Template_is_saved_replaced_and_listed()
    {
        using var db = new FocusCoachDb(":memory:");
        Assert.Empty(db.ListCategories());                     // 実行も登録も無いタスクは出さない
        db.EnsureCategory("使っていない");
        Assert.Empty(db.ListCategories());

        db.SaveTemplate("要件定義", [new("ヒアリング", 30), new("整理", null)]);
        Assert.Equal([new StepItem("ヒアリング", 30), new StepItem("整理", null)], db.LoadTemplate("要件定義"));
        db.SaveTemplate("要件定義", [new("整理", 20)]);         // 丸ごと置き換え
        Assert.Equal([new StepItem("整理", 20)], db.LoadTemplate("要件定義"));

        var c = Assert.Single(db.ListCategories());
        Assert.Equal(("要件定義", 0, (DateTimeOffset?)null, true), (c.Name, c.CompletedRuns, c.LastCompleted, c.HasTemplate));
    }

    [Fact]
    public void Completed_runs_are_listed_newest_first_with_last_run_steps()
    {
        using var db = new FocusCoachDb(":memory:");
        var old = ScenarioRunner.Run("at 00:00 start_task category=\"古いタスク\" subtasks=\"A:5\"\nat 00:10 end_task", runId: "o");
        db.SaveRun(old.Session.State, old.Metrics);
        var run = ScenarioRunner.Run(File.ReadAllText(Path.Combine(TestPaths.Scenarios, "basic_two_subtasks.txt")), runId: "n");
        db.SaveRun(run.Session.State, run.Metrics);
        var aborted = ScenarioRunner.Run("at 00:00 start_task category=\"中止だけ\"\nat 00:10 abort_task", runId: "x");
        db.SaveRun(aborted.Session.State, aborted.Metrics);

        var list = db.ListCategories();
        Assert.Contains(list, c => c.Name == "中止だけ" && c.CompletedRuns == 0);   // 中止した実行だけのタスクも出す(0.10A)
        var names = list.Select(c => c.Name).ToList();
        Assert.Contains("古いタスク", names);
        Assert.Contains(run.Session.State.Category, names);
        Assert.All(list.Where(c => c.Name != "中止だけ"), c => Assert.Equal(1, c.CompletedRuns));
        Assert.Equal(list.OrderByDescending(c => c.LastUsed).Select(c => c.Name), names);

        Assert.Equal([new StepItem("A", 5)], db.LoadLastRunSteps("古いタスク"));
        var last = db.LoadLastRunSteps(run.Session.State.Category);
        Assert.Equal(run.Session.State.Subtasks.Select(s => s.Name), last.Select(s => s.Name));
        Assert.Empty(db.LoadLastRunSteps("中止だけ"));
    }

    [Fact]
    public void Schema_v3_file_is_migrated_to_latest()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fc_test_{Guid.NewGuid():N}.db");
        try
        {
            using (new FocusCoachDb(path)) { }
            SqliteConnection.ClearAllPools();
            // v3 の状態に戻す(target_min 列が無い)
            using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "ALTER TABLE subtask_templates DROP COLUMN target_min; ALTER TABLE task_categories DROP COLUMN draft_project; ALTER TABLE task_categories DROP COLUMN draft_steps; ALTER TABLE task_categories DROP COLUMN draft_task_apps; ALTER TABLE task_categories DROP COLUMN draft_first_step; ALTER TABLE task_categories DROP COLUMN last_used_at; PRAGMA user_version=3;";
                cmd.ExecuteNonQuery();
            }
            using (var db = new FocusCoachDb(path))
            {
                Assert.Equal(FocusCoachDb.SchemaVersion, db.UserVersion);
                db.SaveTemplate("T", [new("x", 3)]);
                Assert.Equal([new StepItem("x", 3)], db.LoadTemplate("T"));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) try { File.Delete(f); } catch { }
        }
    }
}
