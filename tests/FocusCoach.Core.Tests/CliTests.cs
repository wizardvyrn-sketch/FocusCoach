using FocusCoach.Cli;
using Xunit;

namespace FocusCoach.Core.Tests;

/// <summary>Console.Out を差し替えるので、並列実行しない。</summary>
[Collection("console")]
public class CliTests
{
    private static async Task<(int Code, string Out)> Fc(params string[] args)
    {
        var sw = new StringWriter();
        var code = await Commands.RunAsync(args, sw);
        return (code, sw.ToString());
    }

    [Fact]
    public async Task Scenario_command_returns_zero_on_pass_and_one_on_failed_expect()
    {
        var ok = await Fc("scenario", Path.Combine(TestPaths.Scenarios, "basic_two_subtasks.txt"));
        Assert.Equal(0, ok.Code);
        Assert.Contains("OK (", ok.Out);

        var bad = Path.Combine(Path.GetTempPath(), $"fc_bad_{Guid.NewGuid():N}.txt");
        File.WriteAllText(bad, "at 00:00 start_task category=x\nat 00:10 end_task\nexpect metrics.total_sec == 1m");
        try
        {
            var r = await Fc("scenario", bad);
            Assert.Equal(1, r.Code);
            Assert.Contains("FAIL", r.Out);
        }
        finally { File.Delete(bad); }
    }

    [Fact]
    public async Task Scenario_parse_error_returns_two()
    {
        var bad = Path.Combine(Path.GetTempPath(), $"fc_bad_{Guid.NewGuid():N}.txt");
        File.WriteAllText(bad, "start_task category=x");
        try { Assert.Equal(2, (await Fc("scenario", bad)).Code); }
        finally { File.Delete(bad); }
    }

    [Fact]
    public async Task Replay_matches_scenario_metrics()
    {
        var jsonl = Path.Combine(Path.GetTempPath(), $"fc_cli_{Guid.NewGuid():N}.jsonl");
        try
        {
            var s = await Fc("scenario", Path.Combine(TestPaths.Scenarios, "interrupt_resume.txt"), "--events", jsonl);
            var r = await Fc("replay", jsonl);
            Assert.Equal(0, r.Code);
            string Line(string o) => o.Split('\n').First(l => l.StartsWith("total "));
            Assert.Equal(Line(s.Out), Line(r.Out));
        }
        finally { File.Delete(jsonl); }
    }

    [Fact]
    public async Task Workspace_plan_is_dry_run_and_masks_titles()
    {
        var json = Path.Combine(Path.GetTempPath(), $"fc_win_{Guid.NewGuid():N}.json");
        File.WriteAllText(json, """[{"hwnd":"1","process":"WINWORD.EXE","title":"顧客A 契約書.docx"}]""");
        try
        {
            var r = await Fc("workspace", "plan", json, "--mode", "start");
            Assert.Equal(0, r.Code);
            Assert.Contains("dry-run", r.Out);
            Assert.Contains("Minimize", r.Out);
            Assert.DoesNotContain("顧客A", r.Out); // タイトルは出さない(08章 §8.4)
        }
        finally { File.Delete(json); }
    }

    [Fact]
    public async Task Diag_prints_no_personal_data_and_exits_zero()
    {
        var r = await Fc("diag", "--data-root", Path.Combine(Path.GetTempPath(), "fc_no_such_dir"));
        Assert.Equal(0, r.Code);
        Assert.Contains("app_version", r.Out);
        Assert.DoesNotContain(Environment.UserName + "/", r.Out);
    }

    [Fact]
    public async Task Unknown_command_returns_two()
    {
        Assert.Equal(2, (await Fc("bogus")).Code);
        Assert.Equal(2, (await Fc()).Code);
    }
}
