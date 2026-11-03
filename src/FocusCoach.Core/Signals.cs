namespace FocusCoach.Core;

/// <summary>ミニパネルの縁の表示状態(09章 §9.4.4)。ポップアップは出さず、これだけで知らせる。</summary>
public enum PanelEdge { Normal, Overrun, InterruptApp, Paused }

public sealed record PanelStatus(PanelEdge Edge, string Reason);

public static class Signals
{
    /// <summary>優先順: 一時停止 &gt; 割り込みアプリ滞在 &gt; 目安超過 &gt; 通常。</summary>
    public static PanelStatus Evaluate(RunState s, DateTimeOffset now, IEnumerable<string>? interruptApps = null)
    {
        if (s.Status != RunStatus.Running) return new(PanelEdge.Normal, "not running");
        if (s.IsPaused) return new(PanelEdge.Paused, s.OpenPause!.Reason);

        var apps = interruptApps ?? TaskWindowClassifier.DefaultInterruptApps;
        if (s.CurrentForeground is { TaskWindow: false } fg && TaskWindowClassifier.IsInterruptApp(fg.Process, apps))
        {
            var dwell = (now - fg.Start).TotalSeconds;
            if (dwell >= s.Params["interrupt_app_dwell_sec"])
                return new(PanelEdge.InterruptApp, $"{TaskWindowClassifier.NormalizeProcess(fg.Process)} {dwell:0}s");
        }

        if (s.Active is { TargetSec: { } target } a)
        {
            var m = MetricsCalculator.Compute(s, now);
            var cur = m.Subtasks.FirstOrDefault(x => x.Index == a.Index);
            if (cur is not null && cur.DurationSec > target) return new(PanelEdge.Overrun, $"{cur.DurationSec:0}s > {target:0}s");
        }
        return new(PanelEdge.Normal, "");
    }
}
