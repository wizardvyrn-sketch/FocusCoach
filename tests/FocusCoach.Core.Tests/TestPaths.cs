namespace FocusCoach.Core.Tests;

internal static class TestPaths
{
    public static string RepoRoot
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "tests", "scenarios"))) d = d.Parent;
            return d?.FullName ?? throw new DirectoryNotFoundException("repo root (tests/scenarios) not found");
        }
    }

    public static string Scenarios => Path.Combine(RepoRoot, "tests", "scenarios");
    public static IEnumerable<object[]> ScenarioFiles() =>
        Directory.GetFiles(Scenarios, "*.txt").OrderBy(x => x).Select(f => new object[] { Path.GetFileName(f) });
}
