using FocusCoach.Core.Scenarios;
using FocusCoach.Core.Workspace;
using Xunit;

namespace FocusCoach.Core.Tests;

public class CoreTests
{
    private static Session NewSession(ManualClock? clock = null, SessionOptions? opt = null) =>
        new("t1", clock ?? new ManualClock(ScenarioRunner.Base), null, opt);

    [Fact]
    public void Params_are_range_checked_and_recorded_in_events()
    {
        var s = NewSession();
        Assert.Throws<CommandRejectedException>(() => s.Dispatch(CoreCommand.Of(Cmd.Set, ("name", "idle_threshold_sec"), ("value", "1"))));
        var evs = s.Dispatch(CoreCommand.Of(Cmd.Set, ("name", "idle_threshold_sec"), ("value", "60")));
        Assert.Contains(evs, e => e.Type == Ev.ParamSet && e.Get("old") == "120");
        Assert.Equal(60, s.State.Params.IdleThresholdSec);
    }

    [Fact]
    public void Time_must_not_go_backwards()
    {
        var s = NewSession();
        s.Dispatch(new CoreCommand(Cmd.StartTask, new Dictionary<string, string> { ["category"] = "x" }, ScenarioRunner.Base.AddMinutes(10)));
        Assert.Throws<CommandRejectedException>(() =>
            s.Dispatch(new CoreCommand(Cmd.Pause, new Dictionary<string, string>(), ScenarioRunner.Base.AddMinutes(5))));
    }

    [Fact]
    public void Rejected_command_leaves_state_unchanged()
    {
        var s = NewSession();
        s.Dispatch(CoreCommand.Of(Cmd.StartTask, ("category", "x")));
        var before = s.State.EventCount;
        Assert.Throws<CommandRejectedException>(() => s.Dispatch(CoreCommand.Of(Cmd.Resume)));
        Assert.Equal(before, s.State.EventCount);
    }

    [Fact]
    public void Resource_locations_are_normalized_and_deduplicated()
    {
        var r = ScenarioRunner.Run("""
            at 00:00 start_task category=x
            at 00:01 resource kind=file location="C:\Work\A.xlsx" source=recent
            at 00:02 resource kind=file location="c:\work\a.xlsx" source=window_title
            at 00:03 resource kind=url location="https://example.test/p#frag" source=browser_history
            at 00:04 resource kind=url location="https://example.test/p/" source=browser_history
            """);
        Assert.Empty(r.Errors);
        Assert.Equal(2, r.Session.State.Resources.Count);
        Assert.Equal(TimeSpan.FromMinutes(1), r.Session.State.Resources.Values.First().FirstSeen - ScenarioRunner.Base);
    }

    [Fact]
    public void Resource_exclusion_by_path_prefix_and_domain()
    {
        var ex = new ExclusionRules([
            new(ExclusionKind.PathPrefix, @"C:\private", ExclusionAction.Ignore),
            new(ExclusionKind.UrlDomain, "mail.example.test", ExclusionAction.Ignore)]);
        var r = ScenarioRunner.Run("""
            at 00:00 start_task category=x
            at 00:01 resource kind=file location="C:\private\a.txt"
            at 00:02 resource kind=url location="https://mail.example.test/inbox"
            at 00:03 resource kind=file location="C:\work\b.txt"
            """, options: new SessionOptions { Exclusions = ex });
        Assert.Single(r.Session.State.Resources);
    }

    [Fact]
    public void Classifier_uses_opened_during_and_resource_match()
    {
        var r = ScenarioRunner.Run("""
            at 00:00 start_task category=x
            at 00:01 window_opened hwnd=0x10 process=EXCEL.EXE title="見積.xlsx - Excel"
            at 00:02 focus process=EXCEL.EXE title="見積.xlsx - Excel" hwnd=0x10
            expect foreground.task_window == yes
            at 00:03 focus process=ms-teams.exe title="チャット"
            expect foreground.task_window == no
            at 00:04 resource kind=file location="C:\w\仕様書.docx"
            at 00:05 focus process=WINWORD.EXE title="仕様書.docx - Word" hwnd=0x20
            expect foreground.task_window == yes
            at 00:06 focus process=notepad.exe title="無関係" hwnd=0x30
            expect foreground.task_window == no
            at 00:07 window_closed hwnd=0x10
            at 00:08 focus process=EXCEL.EXE title="別の.xlsx - Excel" hwnd=0x10
            expect foreground.task_window == no
            """);
        Assert.True(r.Passed, string.Join("; ", r.Errors.Concat(r.Checks.Where(c => !c.Passed).Select(c => c.Text))));
    }

    [Fact]
    public void Masked_title_is_not_stored()
    {
        var ex = new ExclusionRules([new(ExclusionKind.Process, "secretapp", ExclusionAction.Mask)]);
        var r = ScenarioRunner.Run("at 00:00 start_task category=x\nat 00:01 focus process=SecretApp.exe title=\"顧客A 契約書\"", options: new SessionOptions { Exclusions = ex });
        Assert.Equal("[masked]", r.Session.State.CurrentForeground!.Title);
        Assert.DoesNotContain(r.Events, e => e.Data.Values.Any(v => v.Contains("顧客A")));
    }

    [Fact]
    public void Abort_closes_open_intervals()
    {
        var r = ScenarioRunner.Run("at 00:00 start_task category=x\nat 00:10 pause\nat 00:20 abort_task\nexpect status == aborted\nexpect metrics.paused_sec == 10m\nexpect subtask[1].duration == 10m");
        Assert.True(r.Passed, string.Join("; ", r.Checks.Where(c => !c.Passed).Select(c => c.Text + c.Message)));
    }

    [Fact]
    public void Score_is_clamped_and_zero_without_activity()
    {
        var r = ScenarioRunner.Run("at 00:00 start_task category=x\nat 00:00 end_task");
        Assert.Equal(0, r.Metrics.FocusScore);
        var many = string.Join("\n", Enumerable.Range(0, 20).Select(i => $"at 00:{i * 2 + 1:00} focus process=a{i}.exe title=t task_window={(i % 2 == 0 ? "yes" : "no")}"));
        var r2 = ScenarioRunner.Run("at 00:00 start_task category=x\n" + many + "\nat 00:59 end_task");
        Assert.InRange(r2.Metrics.FocusScore, 0, 100);
    }

    // ---- ワークスペース(Core の純粋関数) ----
    private static readonly WindowInfo[] Wins =
    [
        new("1", "WINWORD.EXE", "計画書_XX.docx - Word"),
        new("2", "EXCEL.EXE", "見積書.xlsx - Excel"),
        new("3", "chrome.exe", "社内ポータル"),
        new("4", "explorer.exe", "ツール", ToolWindow: true),
        new("5", "msedge.exe", "ダイアログ", Owned: true),
        new("6", "ApplicationFrameHost.exe", "設定", Cloaked: true),
        new("7", "FocusCoach.exe", "FocusCoach"),
        new("8", "notepad.exe", "メモ", Minimized: true),
        new("9", "OUTLOOK.EXE", "受信トレイ", Visible: false),
    ];

    [Fact]
    public void PlanStart_minimizes_only_normal_visible_windows()
    {
        var steps = WorkspacePlanner.PlanStart(Wins, keepProcesses: ["excel"]);
        var act = steps.ToDictionary(s => s.Window.Hwnd, s => s.Action);
        Assert.Equal(WsAction.Minimize, act["1"]);
        Assert.Equal(WsAction.Keep, act["2"]); // keep list
        Assert.Equal(WsAction.Minimize, act["3"]);
        foreach (var k in new[] { "4", "5", "6", "7", "8", "9" }) Assert.Equal(WsAction.Keep, act[k]);
    }

    [Fact]
    public void PlanResume_restores_work_windows_and_minimizes_new_ones_without_closing()
    {
        var snapshot = Wins.Where(w => w.Hwnd is "1" or "2").ToList();
        var current = new[] { Wins[0], Wins[1] with { Minimized = true }, new WindowInfo("20", "chrome.exe", "調べ物"), new WindowInfo("21", "chrome.exe", "最小化済み", Minimized: true) };
        var steps = WorkspacePlanner.PlanResume(snapshot, current);
        Assert.Equal(WsAction.Restore, steps.First(s => s.Window.Hwnd == "1").Action);
        Assert.Equal(WsAction.Restore, steps.First(s => s.Window.Hwnd == "2").Action);
        Assert.Equal(WsAction.Minimize, steps.First(s => s.Window.Hwnd == "20").Action);
        Assert.Equal(WsAction.Keep, steps.First(s => s.Window.Hwnd == "21").Action);
        Assert.DoesNotContain(steps, s => s.Action == WsAction.Close); // U-09-2: 閉じない
    }

    [Fact]
    public void PlanFinish_keeps_deliverables_unchecked()
    {
        var opened = new[]
        {
            new WindowInfo("1", "WINWORD.EXE", "開発計画書_XX.docx - Word"),
            new WindowInfo("2", "EXCEL.EXE", "見積書.xlsx - Excel"),
            new WindowInfo("3", "chrome.exe", "社内ポータル - XX"),
            new WindowInfo("4", "WINWORD.EXE", "別の文書.docx - Word"),
            new WindowInfo("5", "explorer.exe", "ツール", ToolWindow: true),
        };
        var steps = WorkspacePlanner.PlanFinish(opened, "XX", lastActiveHwnd: "2");
        var act = steps.ToDictionary(s => s.Window.Hwnd, s => s.Action);
        Assert.Equal(WsAction.Keep, act["1"]);   // プロジェクト名を含む Word
        Assert.Equal(WsAction.Keep, act["2"]);   // 最後にアクティブだった Office 文書
        Assert.Equal(WsAction.Close, act["3"]);  // ブラウザは Office ではないので閉じる候補
        Assert.Equal(WsAction.Close, act["4"]);
        Assert.Equal(WsAction.Keep, act["5"]);   // 対象外(ツールウィンドウ)は触らない
    }

    [Theory]
    [InlineData(@"C:\work\YY\見積書.xlsx", "見積書.xlsx")]
    [InlineData(@"\fs01\見積\XX", "XX")]
    [InlineData("/home/u/a.txt", "a.txt")]
    [InlineData("plain", "plain")]
    public void FileNameOf_is_os_independent(string loc, string expected) => Assert.Equal(expected, Reducer.FileNameOf(loc));

    // ---- ガイド・比較 ----
    private static RunSummary Run(string id, int day, double total, params (string n, double s, double? search)[] subs) =>
        new(id, new DateTimeOffset(2026, 1, day, 9, 0, 0, TimeSpan.Zero), total, total, 80, 2, subs.Select(x => new SubtaskSummary(x.n, x.s, x.search)).ToList());

    [Fact]
    public void Guide_uses_median_times_ratio_and_reports_confidence()
    {
        var p = new ParamRegistry();
        Assert.Equal("—", Guide.Build([], p).Confidence);
        var hist = new[]
        {
            Run("a", 1, 6000, ("確認", 1000, null), ("執筆", 5000, null)),
            Run("b", 2, 5400, ("確認", 1200, null), ("執筆", 4200, null)),
            Run("c", 3, 7200, ("確認", 2000, null), ("執筆", 5200, null)),
        };
        var g = Guide.Build(hist, p);
        Assert.Equal(3, g.SampleCount);
        Assert.Equal("中", g.Confidence);
        Assert.Equal(6000, g.MedianTotalSec);
        Assert.Equal(6000 * 0.95, g.TargetSec!.Value, 6);
        Assert.Equal(["確認", "執筆"], g.Subtasks.Select(x => x.Name));
        Assert.Equal(1200, g.Subtasks[0].MedianSec);
    }

    [Fact]
    public void Compare_yields_at_most_three_improvements_in_blameless_tone()
    {
        var p = new ParamRegistry();
        var hist = new[]
        {
            Run("a", 1, 6000, ("確認", 1000, null), ("執筆", 5000, null)),
            Run("b", 2, 6000, ("確認", 1000, null), ("執筆", 5000, null)),
        };
        var cur = Run("c", 3, 8000, ("確認", 2500, 600), ("執筆", 5500, null)) with { SwitchPerHour = 6 };
        var res = Compare.Build(cur, hist, p);
        Assert.Equal(2 + 2, res.Rows.Count);
        Assert.InRange(res.Improvements.Count, 1, 3);
        Assert.Contains(res.Improvements, t => t.Contains("資料ランチャー"));
        Assert.All(res.Improvements, t => Assert.DoesNotContain("べき", t));
        Assert.Equal(6000, res.Rows[0].Best);
    }
}

public class IntervalTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static Internal.Iv Iv(int a, int b) => new(T0.AddMinutes(a), T0.AddMinutes(b));

    [Fact]
    public void Subtract_removes_overlaps_and_keeps_gaps()
    {
        var set = Internal.IvOps.Merge([Iv(10, 20), Iv(30, 40)]);
        var r = Internal.IvOps.Subtract(Iv(0, 50), set);
        Assert.Equal([Iv(0, 10), Iv(20, 30), Iv(40, 50)], r);
        Assert.Empty(Internal.IvOps.Subtract(Iv(12, 18), set));
        Assert.Equal([Iv(20, 25)], Internal.IvOps.Subtract(Iv(15, 25), set));
    }
}

public class PlatformMappingTests
{
    [Theory]
    [InlineData(0x02B1, 0x7, "away_start", "lock")]
    [InlineData(0x02B1, 0x8, "away_end", "lock")]
    [InlineData(0x02B1, 0x4, "away_start", "remote")]
    [InlineData(0x02B1, 0x3, "away_end", "remote")]
    [InlineData(0x0218, 0x4, "away_start", "sleep")]
    [InlineData(0x0218, 0x12, "away_end", "sleep")]
    [InlineData(0x0218, 0x7, "away_end", "sleep")]
    public void Session_and_power_messages_map_to_away_commands(int msg, long wp, string type, string reason)
    {
        var c = WindowsSessionEvents.Translate(msg, wp)!;
        Assert.Equal(type, c.Type);
        Assert.Equal(reason, c.Get("reason"));
        Assert.Equal("monitor", c.Get("origin"));
    }

    [Fact]
    public void Unrelated_messages_are_ignored()
    {
        Assert.Null(WindowsSessionEvents.Translate(0x02B1, 0x5)); // WTS_SESSION_LOGON
        Assert.Null(WindowsSessionEvents.Translate(0x0218, 0xA)); // PBT_APMPOWERSTATUSCHANGE
        Assert.Null(WindowsSessionEvents.Translate(0x0001, 0));
    }

    [Fact]
    public void Sleep_resume_twice_is_harmless_and_lock_during_sleep_does_not_resume()
    {
        var clock = new ManualClock(Scenarios.ScenarioRunner.Base);
        var s = new Session("p", clock);
        s.Dispatch(CoreCommand.Of(Cmd.StartTask, ("category", "x")));
        foreach (var (m, w, min) in new (int, long, int)[] { (0x0218, 0x4, 10), (0x02B1, 0x8, 70), (0x0218, 0x12, 71), (0x0218, 0x7, 72) })
        {
            clock.Now = Scenarios.ScenarioRunner.Base.AddMinutes(min);
            s.Dispatch(WindowsSessionEvents.Translate(m, w)!);
        }
        Assert.False(s.State.IsPaused);
        var p = Assert.Single(s.State.Pauses);
        Assert.Equal("sleep", p.Reason);
        Assert.Equal(61, (p.End!.Value - p.Start).TotalMinutes, 3);
    }

    [Fact]
    public void LogMask_hides_value_but_is_stable()
    {
        var a = LogMask.Mask("title", "顧客A 契約書.docx - Word");
        Assert.DoesNotContain("顧客", a);
        Assert.StartsWith("title:", a);
        Assert.Equal(a, LogMask.Mask("title", "顧客A 契約書.docx - Word"));
        Assert.NotEqual(a, LogMask.Mask("title", "別の文書"));
    }

    [Fact]
    public async Task Subscriber_exception_does_not_fail_the_command()
    {
        var clock = new ManualClock(Scenarios.ScenarioRunner.Base);
        var session = new Session("x", clock);
        await using var loop = new EventLoop(session, clock);
        var failures = 0;
        loop.SubscriberFailed += _ => Interlocked.Increment(ref failures);
        loop.SnapshotChanged += _ => throw new InvalidOperationException("UI bug");
        var evs = await loop.SendAsync(CoreCommand.Of(Cmd.StartTask, ("category", "x")));
        Assert.NotEmpty(evs);
        await loop.SendAsync(CoreCommand.Of(Cmd.Note, ("text", "n")));
        // 結果は購読者の前に返るので、もう1件送って、前の2件の購読者の処理が済んだことを保証する(ループは直列)
        await loop.SendAsync(CoreCommand.Of(Cmd.Note, ("text", "n2")));
        Assert.Equal(RunStatus.Running, session.State.Status);
        Assert.True(failures >= 2);
    }
}
