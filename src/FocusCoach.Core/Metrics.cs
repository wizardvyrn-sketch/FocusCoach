using FocusCoach.Core.Internal;

namespace FocusCoach.Core;

public sealed record SubtaskMetric(int Index, string Name, string Status, double DurationSec, double? TargetSec, double? SearchSec);

public sealed record RunMetrics(
    int FormulaVersion,
    double TotalSec, double PausedSec, double IdleSec, double ActiveSec,
    double OnTaskSec, double OffTaskSec,
    int SwitchCount, double SwitchPerHour, double FocusScore,
    IReadOnlyList<double?> Timeline,
    IReadOnlyList<SubtaskMetric> Subtasks,
    int InterruptCount, int DeferredCount, double InterruptPausedSec,
    int WorkDays, bool SameDay)
{
    /// <summary>シナリオの expect やCLI表示で名前引きするための平坦化。</summary>
    public IReadOnlyDictionary<string, double> ToDictionary() => new Dictionary<string, double>
    {
        ["total_sec"] = TotalSec, ["paused_sec"] = PausedSec, ["idle_sec"] = IdleSec, ["active_sec"] = ActiveSec,
        ["on_task_sec"] = OnTaskSec, ["off_task_sec"] = OffTaskSec, ["switch_count"] = SwitchCount,
        ["switch_per_hour"] = SwitchPerHour, ["focus_score"] = FocusScore,
        ["interrupt_count"] = InterruptCount, ["deferred_count"] = DeferredCount, ["interrupt_paused_sec"] = InterruptPausedSec,
        ["work_days"] = WorkDays, ["same_day"] = SameDay ? 1 : 0,
    };
}

/// <summary>集中指標の計算(docs/spec/05 F-11)。状態だけから決まる純粋関数なので、リプレイで再計算できる。</summary>
public static class MetricsCalculator
{
    public static RunMetrics Compute(RunState s, DateTimeOffset asOf)
    {
        if (s.StartedAt is not { } start) return Empty();
        var end = s.EndedAt ?? asOf;
        var bounds = new Iv(start, end);
        var p = s.Params;

        var pauses = IvOps.Merge(IvOps.ClipAll(s.Pauses.Select(x => new Iv(x.Start, x.End ?? end)), bounds));

        var idleRaw = s.IdleSpans.Select(x => new Iv(x.Start, x.End ?? end)).ToList();
        if (s.OpenIdleFrom is { } open && (end - open).TotalSeconds >= p.IdleThresholdSec) idleRaw.Add(new Iv(open, end));
        // 一時停止と重なるアイドルは二重に数えない
        var idleOnly = IvOps.Merge(IvOps.ClipAll(idleRaw, bounds));
        var excluded = IvOps.Merge(pauses.Concat(idleOnly));

        var total = bounds.Sec;
        var paused = IvOps.Len(pauses);
        var idle = IvOps.Len(excluded) - paused;
        var active = Math.Max(0, total - paused - idle);

        // 前面ウィンドウの区間。[仮案-09] 最初の focus より前は「オンタスク」とみなす(FocusCoach 自身が前面にいる想定)
        var segs = new List<(Iv Iv, bool Task)>();
        var cursor = start;
        var firstSeg = s.Foreground.FirstOrDefault();
        if (firstSeg is not null && firstSeg.Start > start) segs.Add((new Iv(start, firstSeg.Start), true));
        else if (firstSeg is null) segs.Add((new Iv(start, end), true));
        foreach (var f in s.Foreground)
        {
            var iv = IvOps.Clip(new Iv(f.Start, f.End ?? end), bounds);
            if (iv is not null) segs.Add((iv.Value, f.TaskWindow));
        }

        double OnIn(Iv window) => segs.Where(x => x.Task).Sum(x =>
        {
            var c = IvOps.Clip(x.Iv, window);
            return c is null ? 0 : c.Value.Sec - IvOps.Overlap(c.Value, excluded);
        });

        var onTask = Math.Min(active, OnIn(bounds));
        var offTask = Math.Max(0, active - onTask);

        // 切り替え回数(式 v2): 一時停止・アイドルを除いた「実作業の区間」だけで並べ、オン→オフの変化を数える。
        // 割り込み対応中の行き来は数えない(R-21)。
        var pieces = segs.SelectMany(x => IvOps.Subtract(x.Iv, excluded).Select(iv => (iv, x.Task)))
                         .Where(x => x.iv.Sec > 0).OrderBy(x => x.iv.S).ToList();
        var switches = 0;
        for (var i = 1; i < pieces.Count; i++)
            if (pieces[i - 1].Task && !pieces[i].Task) switches++;
        var perHour = active > 0 ? switches / (active / 3600.0) : 0;
        // 短い実行では1時間あたりの換算が極端になるので、減点を按分する(R-44)
        var weight = Math.Min(1, active / p["score_full_weight_sec"]);
        var penalty = Math.Min(p["focus_switch_penalty_max"], perHour * p["focus_switch_penalty"]) * weight;
        var score = active > 0 ? Math.Clamp(100 * onTask / active - penalty, 0, 100) : 0;

        // 集中度タイムライン: 刻みごとのオンタスク率(アクティブ時間が無い刻みは null)
        var bucket = TimeSpan.FromSeconds(p["timeline_bucket_sec"]);
        var timeline = new List<double?>();
        for (var t = start; t < end; t += bucket)
        {
            var w = new Iv(t, t + bucket < end ? t + bucket : end);
            var act = w.Sec - IvOps.Overlap(w, excluded);
            timeline.Add(act > 0.5 ? Math.Round(OnIn(w) / act, 4) : null);
        }

        var subs = s.Subtasks.Select(st =>
        {
            double dur = 0;
            foreach (var i in st.Intervals)
            {
                var iv = IvOps.Clip(new Iv(i.Start, i.End ?? end), bounds);
                // 式 v2: 一時停止に加えてアイドルも除く(R-15)
                if (iv is not null) dur += iv.Value.Sec - IvOps.Overlap(iv.Value, excluded);
            }
            double? search = null;
            var firstStart = st.Intervals.Count > 0 ? st.Intervals[0].Start : (DateTimeOffset?)null;
            var firstRes = s.Resources.Values.Where(r => r.SubtaskIndex == st.Index).OrderBy(r => r.FirstSeen).FirstOrDefault();
            if (firstStart is { } fs && firstRes is not null) search = Math.Max(0, (firstRes.FirstSeen - fs).TotalSeconds);
            return new SubtaskMetric(st.Index, st.Name, st.Status.ToString().ToLowerInvariant(), dur, st.TargetSec, search);
        }).ToList();

        var intPaused = IvOps.Len(IvOps.Merge(IvOps.ClipAll(
            s.Pauses.Where(x => x.Reason == "interrupt").Select(x => new Iv(x.Start, x.End ?? end)), bounds)));

        // 作業した日数: 実作業の区間がある日付(開始時のオフセットでの暦日)。持ち越しを経た実行は同日内の完了にしない。
        var activeIvs = IvOps.Subtract(bounds, excluded);
        var days = activeIvs.SelectMany(iv => Enumerable.Range(0, (int)(iv.E.Date - iv.S.Date).TotalDays + 1).Select(d => iv.S.Date.AddDays(d))).Distinct().Count();
        var sameDay = s.Status == RunStatus.Completed && start.Date == end.Date && !s.Pauses.Any(x => x.Reason == "suspend");

        return new RunMetrics(AppInfo.FormulaVersion, total, paused, idle, active, onTask, offTask,
            switches, perHour, score, timeline, subs,
            s.Interrupts.Count(i => i.Kind == "now"), s.Deferred.Count, intPaused, days, sameDay);
    }

    private static RunMetrics Empty() => new(AppInfo.FormulaVersion, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], [], 0, 0, 0, 0, false);
}
