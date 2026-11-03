using Xunit;

namespace FocusCoach.Core.Tests;

public class ReentryTests
{
    [Theory]
    [InlineData(600, 40, 60, true, 40, "show")]
    [InlineData(59, 40, 60, false, 0, "short")]      // 短い割り込みでは出さない
    [InlineData(60, 40, 60, true, 40, "show")]       // ちょうど閾値なら出す
    [InlineData(600, 0, 60, false, 0, "off")]        // 0 秒は「出さない」
    [InlineData(600, 2, 60, true, 5, "show")]        // 下限
    [InlineData(600, 9999, 60, true, 600, "show")]   // 上限
    [InlineData(double.NaN, 40, 60, false, 0, "short")]
    public void Decide(double interruptSec, int guideSec, int min, bool show, int sec, string reason)
    {
        var d = ReentryPolicy.Decide(interruptSec, guideSec, min);
        Assert.Equal(show, d.Show);
        Assert.Equal(sec, d.Seconds);
        Assert.Equal(reason, d.Reason);
    }

    [Theory]
    [InlineData(0.5, false)]   // 入力中はフォーカスを移さない
    [InlineData(1.99, false)]
    [InlineData(2, true)]
    [InlineData(30, true)]
    [InlineData(double.NaN, true)]
    public void MayMoveFocus(double sinceInput, bool expected) => Assert.Equal(expected, ReentryPolicy.MayMoveFocus(sinceInput));

    [Fact]
    public void Message_replaces_seconds_and_empty_means_none()
    {
        Assert.Equal("40秒で、元の作業に集中しましょう", ReentryPolicy.Message("{sec}秒で、元の作業に集中しましょう", 40));
        Assert.Equal("", ReentryPolicy.Message("  ", 40));
        Assert.Equal("40秒で、元の作業に集中しましょう", ReentryPolicy.Message(null, 40));   // 未設定は言語ごとの既定文(0.13A)
    }

    [Fact]
    public void Remaining_rounds_up()
    {
        Assert.Equal("0:40", ReentryPolicy.Remaining(40));
        Assert.Equal("0:31", ReentryPolicy.Remaining(30.2));
        Assert.Equal("0:00", ReentryPolicy.Remaining(-3));
        Assert.Equal("1:30", ReentryPolicy.Remaining(90));
    }
}

public class FollowupTests
{
    private static readonly DateOnly Today = new(2026, 10, 29);
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 9, 0, 0, TimeSpan.FromHours(9));

    private static FollowupItem F(long id, DateOnly? due, int createdDay = 0, bool done = false, int doneDay = 0) =>
        new(id, $"item{id}", due, "割込み作業", "", "", T0.AddDays(createdDay), done, done ? T0.AddDays(doneDay) : null);

    [Fact]
    public void Buckets()
    {
        Assert.Equal(DueBucket.Overdue, Followups.Bucket(Today.AddDays(-1), Today));
        Assert.Equal(DueBucket.Today, Followups.Bucket(Today, Today));
        Assert.Equal(DueBucket.Tomorrow, Followups.Bucket(Today.AddDays(1), Today));
        Assert.Equal(DueBucket.Later, Followups.Bucket(Today.AddDays(2), Today));
        Assert.Equal(DueBucket.None, Followups.Bucket(null, Today));
    }

    [Fact]
    public void Sort_overdue_today_tomorrow_later_none_then_done()
    {
        var items = new[]
        {
            F(1, null, createdDay: 2), F(2, Today.AddDays(5)), F(3, Today), F(4, Today.AddDays(-2)),
            F(5, null, createdDay: 1), F(6, Today.AddDays(1)), F(7, Today.AddDays(3)),
            F(8, Today, done: true, doneDay: 1), F(9, null, done: true, doneDay: 3),
        };
        var ids = Followups.Sort(items, Today).Select(i => i.Id).ToArray();
        Assert.Equal([4, 3, 6, 7, 2, 5, 1, 9, 8], ids);   // 期限なしは古い順、完了は新しく完了した順
    }

    [Fact]
    public void Badge_counts_open_and_due()
    {
        Assert.Equal("", Followups.Badge([], Today));
        Assert.Equal("", Followups.Badge([F(1, Today, done: true)], Today));
        Assert.Equal("あとでやること 2", Followups.Badge([F(1, null), F(2, Today.AddDays(1))], Today));
        Assert.Equal("あとでやること 3 ・今日まで 2", Followups.Badge([F(1, null), F(2, Today), F(3, Today.AddDays(-1))], Today));
    }

    [Fact]
    public void Due_labels()
    {
        Assert.Equal("今日", Followups.DueLabel(Today, Today));
        Assert.Equal("明日", Followups.DueLabel(Today.AddDays(1), Today));
        Assert.Equal("11/3", Followups.DueLabel(new DateOnly(2026, 11, 3), Today));
        Assert.Equal("期限切れ 10/27", Followups.DueLabel(new DateOnly(2026, 10, 27), Today));
        Assert.Equal("", Followups.DueLabel(null, Today));
    }

    [Fact]
    public void Parse_due_choice()
    {
        Assert.Null(Followups.ParseDueChoice("none", Today));
        Assert.Null(Followups.ParseDueChoice("", Today));
        Assert.Equal(Today, Followups.ParseDueChoice("today", Today));
        Assert.Equal(Today.AddDays(1), Followups.ParseDueChoice("tomorrow", Today));
        Assert.Equal(new DateOnly(2026, 11, 3), Followups.ParseDueChoice("2026-11-03", Today));
        Assert.Null(Followups.ParseDueChoice("来週", Today));
        Assert.Equal("2026-11-03", Followups.FormatDue(new DateOnly(2026, 11, 3)));
        Assert.Equal("", Followups.FormatDue(null));
    }
}
