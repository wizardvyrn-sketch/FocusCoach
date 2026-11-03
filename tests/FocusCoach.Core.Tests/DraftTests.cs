using FocusCoach.Core.Scenarios;
using FocusCoach.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FocusCoach.Core.Tests;

/// <summary>開始準備の入力の下書きと、プルダウンの一覧(design-0.10A-usability.md R2)。</summary>
public class DraftTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(9));

    private static TaskDraft Draft(string name, DateTimeOffset at, string project = "XX更改", string steps = "A:10,B", string apps = "WINWORD.EXE", string first = "資料を開く")
        => new(name, project, StepList.Parse(steps), apps, first, at);

    [Fact]
    public void Draft_roundtrips_and_second_save_replaces_it()
    {
        using var db = new FocusCoachDb(":memory:");
        Assert.Null(db.LoadDraft("要件定義"));
        db.SaveDraft(Draft("要件定義", T0));
        var d = db.LoadDraft("要件定義")!;
        Assert.Equal("XX更改", d.Project);
        Assert.Equal([new StepItem("A", 10), new StepItem("B", null)], d.Steps);
        Assert.Equal(("WINWORD.EXE", "資料を開く", T0), (d.TaskApps, d.FirstStep, d.SavedAt));

        db.SaveDraft(Draft("要件定義", T0.AddHours(1), project: "YY", steps: "C:5", first: ""));
        var d2 = db.LoadDraft("要件定義")!;
        Assert.Equal(("YY", "", T0.AddHours(1)), (d2.Project, d2.FirstStep, d2.SavedAt));
        Assert.Equal([new StepItem("C", 5)], d2.Steps);
    }

    [Fact]
    public void Blank_category_is_not_saved_and_name_is_trimmed()
    {
        using var db = new FocusCoachDb(":memory:");
        db.SaveDraft(Draft("   ", T0));
        Assert.Empty(db.ListCategories());
        db.SaveDraft(Draft(" 週次報告 ", T0));
        Assert.Equal("週次報告", Assert.Single(db.ListCategories()).Name);
    }

    [Fact]
    public void Draft_only_task_is_listed_so_input_that_was_never_started_is_not_lost()
    {
        using var db = new FocusCoachDb(":memory:");
        db.SaveDraft(Draft("入力だけ", T0));
        var c = Assert.Single(db.ListCategories());
        Assert.Equal(("入力だけ", 0, true, false, T0), (c.Name, c.CompletedRuns, c.HasDraft, c.HasTemplate, c.LastUsed));
    }

    [Fact]
    public void Aborted_and_running_tasks_are_listed_too_not_only_completed_ones()
    {
        using var db = new FocusCoachDb(":memory:");
        var r = ScenarioRunner.Run("at 00:00 start_task category=\"中止した\"\nat 00:10 abort_task", runId: "x");
        db.SaveRun(r.Session.State, r.Metrics);
        var c = Assert.Single(db.ListCategories());
        Assert.Equal(("中止した", 0), (c.Name, c.CompletedRuns));
        Assert.NotNull(c.LastUsed);
    }

    [Fact]
    public void Order_is_last_used_newest_first_mixing_runs_and_drafts()
    {
        using var db = new FocusCoachDb(":memory:");
        var old = ScenarioRunner.Run("at 00:00 start_task category=\"古い実行\"\nat 00:10 end_task", runId: "o");
        db.SaveRun(old.Session.State, old.Metrics);
        db.SaveDraft(Draft("新しい下書き", DateTimeOffset.Now.AddDays(1)));
        db.SaveDraft(Draft("少し前の下書き", DateTimeOffset.Now.AddDays(-400)));
        Assert.Equal(["新しい下書き", "古い実行", "少し前の下書き"], db.ListCategories().Select(c => c.Name).ToList());
    }

    [Fact]
    public void Archived_task_is_hidden_but_keeps_history_and_comes_back_when_used_again()
    {
        using var db = new FocusCoachDb(":memory:");
        var r = ScenarioRunner.Run("at 00:00 start_task category=\"整理する\"\nat 00:10 end_task", runId: "a");
        db.SaveRun(r.Session.State, r.Metrics);
        db.SaveDraft(Draft("残す", T0));
        db.Archive("整理する");
        Assert.Equal(["残す"], db.ListCategories().Select(c => c.Name).ToList());
        Assert.Single(db.LoadHistory("整理する"));                      // 実行の記録は消えない

        db.SaveDraft(Draft("整理する", T0.AddDays(1)));                 // もう一度使うと一覧に戻る
        Assert.Contains(db.ListCategories(), c => c.Name == "整理する");
    }

    [Fact]
    public void Starting_an_archived_task_brings_it_back()
    {
        using var db = new FocusCoachDb(":memory:");
        db.SaveDraft(Draft("戻る", T0));
        db.Archive("戻る");
        Assert.Empty(db.ListCategories());
        var r = ScenarioRunner.Run("at 00:00 start_task category=\"戻る\"\nat 00:10 end_task", runId: "b");
        db.SaveRun(r.Session.State, r.Metrics);
        Assert.Single(db.ListCategories());
    }

    [Fact]
    public void Schema_v4_file_is_migrated_to_latest_and_keeps_existing_data()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fc_test_{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new FocusCoachDb(path)) { db.SaveTemplate("既存", [new("x", 3)]); }
            SqliteConnection.ClearAllPools();
            using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "ALTER TABLE task_categories DROP COLUMN draft_project; ALTER TABLE task_categories DROP COLUMN draft_steps; " +
                                  "ALTER TABLE task_categories DROP COLUMN draft_task_apps; ALTER TABLE task_categories DROP COLUMN draft_first_step; " +
                                  "ALTER TABLE task_categories DROP COLUMN last_used_at; " +
                                  "DROP TABLE followups; DROP TABLE interrupt_records; DROP TABLE interrupt_categories; PRAGMA user_version=4;";
                cmd.ExecuteNonQuery();
            }
            using (var db = new FocusCoachDb(path))
            {
                Assert.Equal(FocusCoachDb.SchemaVersion, db.UserVersion);
                Assert.Equal([new StepItem("x", 3)], db.LoadTemplate("既存"));   // 登録済みの手順は残る
                db.SaveDraft(Draft("既存", T0));
                Assert.Equal("XX更改", db.LoadDraft("既存")!.Project);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) try { File.Delete(f); } catch { }
        }
    }
}
