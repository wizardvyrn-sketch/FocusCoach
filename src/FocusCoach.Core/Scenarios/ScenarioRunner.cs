using System.Globalization;

namespace FocusCoach.Core.Scenarios;

public sealed record CheckResult(int Line, string Text, bool Passed, string Actual, string? Message);

public sealed class ScenarioResult
{
    public required Session Session { get; init; }
    public required List<CoreEvent> Events { get; init; }
    public required List<CheckResult> Checks { get; init; }
    public required List<string> Errors { get; init; }
    public required DateTimeOffset LastAt { get; init; }
    public RunMetrics Metrics => MetricsCalculator.Compute(Session.State, LastAt);
    public bool Passed => Errors.Count == 0 && Checks.All(c => c.Passed);
}

public sealed class ListSink : IEventSink
{
    public List<CoreEvent> Events { get; } = [];
    public void Append(CoreEvent e) => Events.Add(e);
}

/// <summary>シナリオを仮想時刻で実行する。実時間は待たない。</summary>
public static class ScenarioRunner
{
    /// <summary>仮想時刻の基準(架空の日時)。</summary>
    public static readonly DateTimeOffset Base = new(2026, 1, 5, 9, 0, 0, TimeSpan.FromHours(9));

    public static ScenarioResult Run(string text, IEventSink? extraSink = null, SessionOptions? options = null, string runId = "scenario")
    {
        var steps = ScenarioParser.Parse(text);
        var clock = new ManualClock(Base);
        var list = new ListSink();
        var sink = extraSink is null ? (IEventSink)list : new TeeSink(list, extraSink);
        var session = new Session(runId, clock, sink, options);
        var checks = new List<CheckResult>();
        var errors = new List<string>();
        var last = Base;

        foreach (var step in steps)
        {
            switch (step)
            {
                case CommandStep c:
                {
                    var at = Base + c.At;
                    clock.Now = at;
                    last = at;
                    try
                    {
                        session.Dispatch(new CoreCommand(c.Command, c.Args, at));
                        if (c.MustReject) errors.Add($"line {c.Line}: expected rejection but accepted: {c.Text}");
                    }
                    catch (CommandRejectedException ex)
                    {
                        if (!c.MustReject) errors.Add($"line {c.Line}: rejected: {ex.Message}");
                    }
                    catch (Exception ex) when (ex is FormatException or KeyNotFoundException or ArgumentException)
                    {
                        errors.Add($"line {c.Line}: bad arguments: {ex.Message}");
                    }
                    break;
                }
                case ExpectStep e:
                {
                    var now = e.At is { } t ? Base + t : last;
                    checks.Add(Evaluate(session.State, now, list.Events, e));
                    break;
                }
            }
        }
        return new ScenarioResult { Session = session, Events = list.Events, Checks = checks, Errors = errors, LastAt = last };
    }

    private sealed class TeeSink(params IEventSink[] sinks) : IEventSink
    {
        public void Append(CoreEvent e) { foreach (var s in sinks) s.Append(e); }
    }

    private static CheckResult Evaluate(RunState s, DateTimeOffset now, IReadOnlyList<CoreEvent> events, ExpectStep e)
    {
        object? actual;
        try { actual = Resolve(s, now, events, e.Path); }
        catch (Exception ex) { return new CheckResult(e.Line, e.Text, false, "?", ex.Message); }
        if (actual is null) return new CheckResult(e.Line, e.Text, false, "?", $"unknown path: {e.Path}");

        var actualStr = actual is double d ? d.ToString("0.###", CultureInfo.InvariantCulture) : actual.ToString() ?? "";
        var expectedNum = ScenarioParser.ParseQuantity(e.Value);
        bool ok;
        if (actual is double a && expectedNum is { } x)
        {
            ok = e.Op switch
            {
                "==" => Math.Abs(a - x) < 1e-6,
                "~=" => Math.Abs(a - x) <= 0.5,
                "!=" => Math.Abs(a - x) >= 1e-6,
                ">=" => a >= x, "<=" => a <= x, ">" => a > x, "<" => a < x,
                _ => throw new ScenarioParseException(e.Line, $"bad operator: {e.Op}"),
            };
        }
        else
        {
            ok = e.Op switch
            {
                "==" => actualStr == e.Value,
                "!=" => actualStr != e.Value,
                _ => throw new ScenarioParseException(e.Line, $"operator {e.Op} needs numbers (actual: {actualStr})"),
            };
        }
        return new CheckResult(e.Line, e.Text, ok, actualStr, ok ? null : $"actual {actualStr}");
    }

    /// <summary>expect で使えるパス。数値は double、それ以外は string で返す。</summary>
    private static object? Resolve(RunState s, DateTimeOffset now, IReadOnlyList<CoreEvent> events, string path)
    {
        var m = MetricsCalculator.Compute(s, now);
        if (path.StartsWith("metrics.", StringComparison.Ordinal))
            return m.ToDictionary().TryGetValue(path[8..], out var v) ? v : null;
        if (path.StartsWith("params.", StringComparison.Ordinal))
            return s.Params.All.TryGetValue(path[7..], out var pv) ? pv : null;

        if (path.StartsWith("subtask[", StringComparison.Ordinal))
        {
            var close = path.IndexOf(']');
            var n = int.Parse(path[8..close], CultureInfo.InvariantCulture) - 1; // 1始まり
            var field = path[(close + 2)..];
            if (n < 0 || n >= m.Subtasks.Count) throw new InvalidOperationException($"no subtask[{n + 1}]");
            var sm = m.Subtasks[n];
            return field switch
            {
                "duration" => sm.DurationSec,
                "name" => sm.Name,
                "status" => sm.Status,
                "target" => sm.TargetSec ?? -1,
                "search" => sm.SearchSec ?? -1,
                "resume_note" => s.Subtasks[n].ResumeNote ?? "",
                "next_step" => s.Subtasks[n].NextStep ?? "",
                _ => null,
            };
        }

        // events.<type>: その種類のイベントの件数(例: events.resume_card_shown)
        if (path.StartsWith("events.", StringComparison.Ordinal) && path != "events.count")
            return (double)events.Count(x => x.Type == path[7..]);

        var card = ResumeCard.Build(s, now);
        return path switch
        {
            "resources.count" => (double)s.Resources.Count,
            "interrupts.count" => (double)s.Interrupts.Count,
            "deferred.count" => (double)s.Deferred.Count,
            "tray.count" => (double)s.Tray.Count,
            "notes.count" => (double)s.Notes.Count,
            "opened_during.count" => (double)s.OpenedDuring.Count,
            "subtasks.count" => (double)s.Subtasks.Count,
            "events.count" => (double)events.Count,
            "pauses.count" => (double)s.Pauses.Count,
            "last_pause.reason" => s.Pauses.Count > 0 ? s.Pauses.MaxBy(x => x.Start)!.Reason : "",
            "idle_spans.count" => (double)s.IdleSpans.Count,
            "status" => s.Status.ToString().ToLowerInvariant(),
            "paused" => s.IsPaused ? "yes" : "no",
            "edge" => Signals.Evaluate(s, now).Edge.ToString(),
            "resume_card.subtask" => card.SubtaskName,
            "resume_card.note" => card.ResumeNote ?? "",
            "resume_card.next" => card.NextStep ?? "",
            "resume_card.subtask_after" => card.SubtaskName,
            "first_step" => s.FirstStep,
            "panel.first_step" => StateSnapshot.From(s, now).FirstStep,
            "resume_card.resources" => (double)card.Resources.Count,
            "resume_card.paused_sec" => card.PausedSec,
            "interrupt_closes.count" => (double)s.InterruptCloses.Count,
            "last_close.category" => s.InterruptCloses.LastOrDefault()?.Category ?? "",
            "last_close.outcome" => s.InterruptCloses.LastOrDefault()?.Outcome ?? "",
            "last_close.followup" => s.InterruptCloses.LastOrDefault()?.Followup ?? "",
            "last_close.due" => s.InterruptCloses.LastOrDefault()?.Due ?? "",
            "last_close.interrupt_sec" => s.InterruptCloses.LastOrDefault()?.InterruptSec ?? -1,
            "followups.count" => (double)s.InterruptCloses.Count(x => x.Followup.Length > 0),
            "reentries.count" => (double)s.Reentries.Count,
            "last_reentry.how" => s.Reentries.LastOrDefault()?.How ?? "",
            "last_reentry.shown_sec" => s.Reentries.LastOrDefault()?.ShownSec ?? -1,
            "foreground.task_window" => s.CurrentForeground is { } f ? (f.TaskWindow ? "yes" : "no") : "none",
            _ => null,
        };
    }
}
