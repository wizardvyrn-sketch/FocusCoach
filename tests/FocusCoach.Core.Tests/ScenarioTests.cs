using FocusCoach.Core.Scenarios;
using FocusCoach.Storage;
using Xunit;

namespace FocusCoach.Core.Tests;

public class ScenarioTests
{
    [Theory]
    [MemberData(nameof(TestPaths.ScenarioFiles), MemberType = typeof(TestPaths))]
    public void Scenario_passes(string file)
    {
        var r = ScenarioRunner.Run(File.ReadAllText(Path.Combine(TestPaths.Scenarios, file)));
        var msg = string.Join("\n", r.Errors.Concat(r.Checks.Where(c => !c.Passed).Select(c => $"line {c.Line}: {c.Text} -> {c.Message}")));
        Assert.True(r.Passed, msg);
        Assert.NotEmpty(r.Checks);
    }

    /// <summary>08章 §8.1-1: イベント列だけから、同じ状態と指標が再現できる(JSONL往復を含む)。</summary>
    [Theory]
    [MemberData(nameof(TestPaths.ScenarioFiles), MemberType = typeof(TestPaths))]
    public void Replay_from_jsonl_reproduces_metrics(string file)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fc_test_{Guid.NewGuid():N}.jsonl");
        try
        {
            ScenarioResult r;
            using (var w = new JsonlEventWriter(path)) r = ScenarioRunner.Run(File.ReadAllText(Path.Combine(TestPaths.Scenarios, file)), w);

            var replayed = Reducer.Replay(JsonlEventReader.Read(path));
            var a = r.Metrics.ToDictionary();
            var b = MetricsCalculator.Compute(replayed, r.LastAt).ToDictionary();
            foreach (var (k, v) in a) Assert.Equal(v, b[k], 6);
            Assert.Equal(r.Session.State.Params.All, replayed.Params.All);
            Assert.Equal(r.Session.State.EventCount, replayed.EventCount);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parser_handles_quotes_comments_and_units()
    {
        var steps = ScenarioParser.Parse("at 00:10 resource kind=file location=\"C:\\a b\\c.xlsx\" # comment\nexpect subtask[1].duration == 1h30m");
        var c = Assert.IsType<CommandStep>(steps[0]);
        Assert.Equal("C:\\a b\\c.xlsx", c.Args["location"]);
        Assert.Equal(TimeSpan.FromMinutes(10), c.At);
        Assert.Equal(5400, ScenarioParser.ParseQuantity("1h30m"));
        Assert.Null(ScenarioParser.ParseQuantity("abc"));
    }

    [Fact]
    public void Parser_rejects_command_without_time()
    {
        Assert.Throws<ScenarioParseException>(() => ScenarioParser.Parse("start_task category=x"));
    }

    [Fact]
    public void Failed_expect_is_reported()
    {
        var r = ScenarioRunner.Run("at 00:00 start_task category=x\nat 00:10 end_task\nexpect metrics.total_sec == 5m");
        Assert.False(r.Passed);
        Assert.Contains("600", r.Checks[0].Actual);
    }
}
