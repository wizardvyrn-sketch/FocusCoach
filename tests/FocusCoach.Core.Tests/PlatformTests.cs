using FocusCoach.Core.Workspace;
using Xunit;

namespace FocusCoach.Core.Tests;

public class PlatformTests
{
    private static readonly WindowInfo[] Wins =
    [
        new("1", "WINWORD.EXE", "a.docx - Word"),
        new("2", "chrome.exe", "ポータル"),
        new("3", "notepad.exe", "メモ"),
    ];

    [Fact]
    public void DryRun_touches_nothing()
    {
        var ws = new FakeWindowSystem(Wins);
        var res = WorkspaceExecutor.Execute(WorkspacePlanner.PlanStart(Wins), ws, dryRun: true);
        Assert.Empty(ws.Log);
        Assert.All(res, r => Assert.True(r.Detail is "dry-run" or "skip"));
        Assert.All(ws.Enumerate(), w => Assert.False(w.Minimized));
    }

    [Fact]
    public void Execute_minimizes_then_restores_and_survives_a_failing_window()
    {
        var ws = new FakeWindowSystem(Wins);
        ws.FailOn.Add("2");
        var res = WorkspaceExecutor.Execute(WorkspacePlanner.PlanStart(Wins), ws, dryRun: false);
        Assert.Equal(["minimize:1", "minimize:2", "minimize:3"], ws.Log);
        Assert.False(res.First(r => r.Step.Window.Hwnd == "2").Done);
        Assert.True(res.First(r => r.Step.Window.Hwnd == "1").Done);
        Assert.True(ws.Enumerate().First(w => w.Hwnd == "1").Minimized);

        ws.Log.Clear();
        var back = WorkspacePlanner.PlanResume(Wins.Where(w => w.Hwnd == "1").ToList(), ws.Enumerate());
        WorkspaceExecutor.Execute(back, ws, dryRun: false);
        Assert.False(ws.Enumerate().First(w => w.Hwnd == "1").Minimized);
        Assert.DoesNotContain(ws.Log, l => l.StartsWith("close"));
    }
}
