namespace FocusCoach.Core;

public sealed record SubtaskSummary(string Name, double Sec, double? SearchSec);

/// <summary>過去の実行の要約。Storage が DB から作る。比較・ガイドの入力。</summary>
public sealed record RunSummary(
    string RunId, DateTimeOffset StartedAt, double TotalSec, double ActiveSec, double FocusScore, double SwitchPerHour,
    IReadOnlyList<SubtaskSummary> Subtasks)
{
    public static RunSummary From(RunState s, RunMetrics m) => new(
        s.RunId, s.StartedAt ?? DateTimeOffset.MinValue, m.TotalSec, m.ActiveSec, m.FocusScore, m.SwitchPerHour,
        m.Subtasks.Where(x => x.Status == "done").Select(x => new SubtaskSummary(x.Name, x.DurationSec, x.SearchSec)).ToList());
}

public sealed record GuideSubtask(string Name, double MedianSec, double TargetSec);
public sealed record GuidePlan(int SampleCount, double? MedianTotalSec, double? TargetSec, IReadOnlyList<GuideSubtask> Subtasks, string Confidence);

public static class Stats
{
    public static double Median(IReadOnlyCollection<double> xs)
    {
        if (xs.Count == 0) throw new InvalidOperationException("empty");
        var a = xs.OrderBy(x => x).ToArray();
        return a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2;
    }
}

/// <summary>開始前ガイド(docs/spec/05 F-02)。過去実績の中央値 × target_ratio を目安にする。</summary>
public static class Guide
{
    public static GuidePlan Build(IReadOnlyList<RunSummary> history, ParamRegistry p)
    {
        var recent = history.OrderByDescending(h => h.StartedAt).Take((int)p["guide_history_n"]).ToList();
        if (recent.Count == 0) return new GuidePlan(0, null, null, [], "—");

        var ratio = p["target_ratio"];
        var medTotal = Stats.Median(recent.Select(r => r.TotalSec).ToList());

        // [仮案-10] サブタスクは名前の完全一致で突き合わせる。表記ゆれ(aliases)の吸収は再分類(F-13)の実装時に追加する。
        // 並びは最新の実行の順序を優先し、それ以外は後ろに足す。
        var order = recent.SelectMany(r => r.Subtasks.Select(x => x.Name)).Distinct().ToList();
        var latest = recent[0].Subtasks.Select(x => x.Name).ToList();
        order = latest.Concat(order.Except(latest)).ToList();

        var subs = order.Select(name =>
        {
            var med = Stats.Median(recent.SelectMany(r => r.Subtasks.Where(x => x.Name == name).Select(x => x.Sec)).ToList());
            return new GuideSubtask(name, med, med * ratio);
        }).ToList();

        var conf = Loc.T(recent.Count switch { >= 5 => "guide.conf.high", >= 3 => "guide.conf.mid", _ => "guide.conf.low" });
        return new GuidePlan(recent.Count, medTotal, medTotal * ratio, subs, conf);
    }
}

public sealed record CompareRow(string Label, double Current, double? Previous, double? Median, double? Best);
public sealed record ComparisonResult(IReadOnlyList<CompareRow> Rows, IReadOnlyList<string> Improvements);

/// <summary>振り返りの比較と改善ポイント(docs/spec/05 F-11)。文面は責めないトーン(06章)。</summary>
public static class Compare
{
    public static ComparisonResult Build(RunSummary current, IReadOnlyList<RunSummary> history, ParamRegistry p)
    {
        var past = history.Where(h => h.RunId != current.RunId).OrderByDescending(h => h.StartedAt).ToList();
        var prev = past.FirstOrDefault();
        var recent = past.Take((int)p["guide_history_n"]).ToList();

        double? Med(Func<RunSummary, double> f) => recent.Count > 0 ? Stats.Median(recent.Select(f).ToList()) : null;
        double? Best(Func<RunSummary, double> f) => past.Count > 0 ? past.Min(f) : null;

        var rows = new List<CompareRow>
        {
            new(Loc.T("metric.total"), current.TotalSec, prev?.TotalSec, Med(r => r.TotalSec), Best(r => r.TotalSec)),
            new(Loc.T("metric.active"), current.ActiveSec, prev?.ActiveSec, Med(r => r.ActiveSec), Best(r => r.ActiveSec)),
        };
        foreach (var sub in current.Subtasks)
        {
            double? Pv(RunSummary r) => r.Subtasks.FirstOrDefault(x => x.Name == sub.Name)?.Sec;
            var pastVals = recent.Select(Pv).Where(v => v is not null).Select(v => v!.Value).ToList();
            var allVals = past.Select(Pv).Where(v => v is not null).Select(v => v!.Value).ToList();
            rows.Add(new(sub.Name, sub.Sec, prev is null ? null : Pv(prev),
                pastVals.Count > 0 ? Stats.Median(pastVals) : null, allVals.Count > 0 ? allVals.Min() : null));
        }

        // 改善ポイント: (影響の大きさ, 文面)。大きい順に最大3件。
        var cands = new List<(double Impact, string Text)>();
        foreach (var r in rows.Skip(2).Where(r => r.Median is not null))
        {
            var d = r.Current - r.Median!.Value;
            if (Math.Abs(d) < p["improve_min_delta_sec"]) continue;
            cands.Add((Math.Abs(d), d > 0
                ? Loc.T("guide.longer", r.Label, Fmt(d))
                : Loc.T("guide.shorter", r.Label, Fmt(-d))));
        }
        foreach (var sub in current.Subtasks.Where(x => x.SearchSec is { } v && v >= p["search_long_sec"]))
            cands.Add((sub.SearchSec!.Value, Loc.T("guide.search", sub.Name, Fmt(sub.SearchSec!.Value))));
        var medSw = Med(r => r.SwitchPerHour);
        if (medSw is { } sw && current.SwitchPerHour > sw + 1)
            cands.Add(((current.SwitchPerHour - sw) * 60, Loc.T("guide.switch", current.SwitchPerHour, sw)));

        return new ComparisonResult(rows, cands.OrderByDescending(c => c.Impact).Take(3).Select(c => c.Text).ToList());
    }

    public static string Fmt(double sec) => Loc.Duration(sec);
}
