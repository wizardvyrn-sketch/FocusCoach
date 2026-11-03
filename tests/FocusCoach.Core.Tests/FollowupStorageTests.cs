using FocusCoach.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FocusCoach.Core.Tests;

/// <summary>スキーマ v6: 割り込みのカテゴリ・記録・あとでやること(design-0.12A §6.2)。</summary>
public class FollowupStorageTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 29, 10, 0, 0, TimeSpan.FromHours(9));

    private static InterruptCloseRecord Close(string category, string followup = "", DateOnly? due = null, int minute = 0) =>
        new("run1", T0.AddMinutes(minute), T0.AddMinutes(minute + 12), category, "A社 旧版", followup.Length > 0 ? "followup" : "done",
            followup, due, "開発計画書作成", followup.Length > 0 ? "[{\"process\":\"OUTLOOK.EXE\"}]" : null);

    [Fact]
    public void New_db_has_only_the_default_category()
    {
        using var db = new FocusCoachDb(":memory:");
        Assert.Equal(6, db.UserVersion);
        Assert.Equal([InterruptCategories.Default], db.ListInterruptCategories());
    }

    [Fact]
    public void Categories_are_added_ordered_by_last_use_and_can_be_archived()
    {
        using var db = new FocusCoachDb(":memory:");
        db.SaveInterruptClose(Close("営業"));
        db.SaveInterruptClose(Close("現場", minute: 30));
        Assert.Equal(["現場", "営業", InterruptCategories.Default], db.ListInterruptCategories());
        db.ArchiveInterruptCategory("営業");
        Assert.Equal(["現場", InterruptCategories.Default], db.ListInterruptCategories());
        db.SaveInterruptClose(Close("営業", minute: 60));   // 使えば一覧に戻る
        Assert.Equal("営業", db.ListInterruptCategories()[0]);
        db.SaveInterruptClose(Close("  ", minute: 90));     // 空は既定のカテゴリ
        Assert.Equal(InterruptCategories.Default, db.ListInterruptCategories()[0]);
    }

    [Fact]
    public void Close_with_followup_creates_an_open_item_and_status_changes()
    {
        using var db = new FocusCoachDb(":memory:");
        var (iid, fid) = db.SaveInterruptClose(Close("営業", "来週の版で直るか確認して返信", new DateOnly(2026, 10, 30)));
        var (_, none) = db.SaveInterruptClose(Close("営業", minute: 20));
        Assert.True(iid > 0);
        Assert.NotNull(fid);
        Assert.Null(none);   // 完了なら作らない

        var items = db.ListFollowups(T0.AddHours(1));
        var f = Assert.Single(items);
        Assert.Equal("来週の版で直るか確認して返信", f.Text);
        Assert.Equal(new DateOnly(2026, 10, 30), f.Due);
        Assert.Equal("営業", f.Category);
        Assert.Equal("A社 旧版", f.Title);
        Assert.Equal("開発計画書作成", f.TaskName);
        Assert.False(f.Done);

        db.SetFollowupStatus(f.Id, "done", T0.AddHours(2));
        Assert.True(Assert.Single(db.ListFollowups(T0.AddHours(3))).Done);
        Assert.Empty(db.ListFollowups(T0.AddDays(8)));               // 完了は7日だけ見せる
        db.SetFollowupStatus(f.Id, "open", T0.AddHours(4));          // 取り消し
        Assert.False(Assert.Single(db.ListFollowups(T0.AddDays(8))).Done);
        db.SetFollowupStatus(f.Id, "deleted", T0.AddHours(5));
        Assert.Empty(db.ListFollowups(T0.AddHours(6)));
        Assert.Throws<ArgumentException>(() => db.SetFollowupStatus(f.Id, "gone", T0));
    }

    [Fact]
    public void Manual_followup_and_reentry_are_saved()
    {
        using var db = new FocusCoachDb(":memory:");
        db.AddFollowup(" 社内ツールの手順をまとめる ", null, "manual", T0);
        Assert.Equal("社内ツールの手順をまとめる", Assert.Single(db.ListFollowups(T0)).Text);
        var (iid, _) = db.SaveInterruptClose(Close("営業"));
        db.SetReentry(iid, "timeout", 40);   // 例外にならないこと
    }

    [Fact]
    public void Schema_v5_file_is_migrated_to_v6()
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
                cmd.CommandText = "DROP TABLE followups; DROP TABLE interrupt_records; DROP TABLE interrupt_categories; PRAGMA user_version=5;";
                cmd.ExecuteNonQuery();
            }
            using (var db = new FocusCoachDb(path))
            {
                Assert.Equal(6, db.UserVersion);
                Assert.Equal([new StepItem("x", 3)], db.LoadTemplate("既存"));
                Assert.Equal([InterruptCategories.Default], db.ListInterruptCategories());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) try { File.Delete(f); } catch { }
        }
    }
}
