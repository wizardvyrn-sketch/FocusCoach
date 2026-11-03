using FocusCoach.Core.Scenarios;
using Xunit;

namespace FocusCoach.Core.Tests;

public class EventLoopTests
{
    [Fact]
    public async Task Commands_from_many_threads_are_processed_serially()
    {
        var clock = new ManualClock(ScenarioRunner.Base);
        var sink = new ListSink();
        var session = new Session("loop", clock, sink);
        await using var loop = new EventLoop(session, clock);
        int inside = 0, maxInside = 0;
        loop.EventsEmitted += _ =>
        {
            var n = Interlocked.Increment(ref inside);
            lock (sink) maxInside = Math.Max(maxInside, n);
            Thread.Sleep(0);
            Interlocked.Decrement(ref inside);
        };

        await loop.SendAsync(CoreCommand.Of(Cmd.StartTask, ("category", "x")));
        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(() =>
            loop.SendAsync(CoreCommand.Of(Cmd.Note, ("text", $"n{i}"))))));
        await loop.SendAsync(CoreCommand.Of(Cmd.EndTask));

        Assert.Equal(1, maxInside);
        Assert.Equal(200, session.State.Notes.Count);
        // seq は欠番なく単調増加
        Assert.Equal(Enumerable.Range(1, sink.Events.Count).Select(i => (long)i), sink.Events.Select(e => e.Seq));
    }

    [Fact]
    public async Task Rejected_command_does_not_stop_the_loop()
    {
        var clock = new ManualClock(ScenarioRunner.Base);
        var session = new Session("loop", clock);
        await using var loop = new EventLoop(session, clock);
        var rejected = 0;
        loop.CommandRejected += (_, _) => Interlocked.Increment(ref rejected);

        await Assert.ThrowsAsync<CommandRejectedException>(() => loop.SendAsync(CoreCommand.Of(Cmd.Pause)));
        var evs = await loop.SendAsync(CoreCommand.Of(Cmd.StartTask, ("category", "x")));
        Assert.NotEmpty(evs);
        Assert.Equal(1, rejected);
    }

    [Fact]
    public async Task Snapshot_is_published_after_each_command()
    {
        var clock = new ManualClock(ScenarioRunner.Base);
        var session = new Session("loop", clock);
        await using var loop = new EventLoop(session, clock);
        StateSnapshot? last = null;
        loop.SnapshotChanged += s => last = s;
        await loop.SendAsync(CoreCommand.Of(Cmd.StartTask, ("category", "x"), ("subtasks", "A,B")));
        await loop.SendAsync(CoreCommand.Of(Cmd.Interrupt, ("kind", "now"), ("note", "メモ")));
        await loop.SendAsync(CoreCommand.Of("__tick")); // tick は未知コマンド扱いにならない
        Assert.NotNull(last);
        Assert.True(last!.Paused);
        Assert.Equal(PanelEdge.Paused, last.Edge);
        Assert.Equal("A", last.SubtaskName);
        Assert.Equal(2, last.SubtaskCount);
    }
}
