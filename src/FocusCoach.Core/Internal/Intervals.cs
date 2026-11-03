namespace FocusCoach.Core.Internal;

internal readonly record struct Iv(DateTimeOffset S, DateTimeOffset E)
{
    public double Sec => Math.Max(0, (E - S).TotalSeconds);
}

internal static class IvOps
{
    public static List<Iv> Merge(IEnumerable<Iv> src)
    {
        var sorted = src.Where(i => i.E > i.S).OrderBy(i => i.S).ToList();
        var res = new List<Iv>();
        foreach (var i in sorted)
        {
            if (res.Count > 0 && i.S <= res[^1].E)
                res[^1] = new Iv(res[^1].S, i.E > res[^1].E ? i.E : res[^1].E);
            else
                res.Add(i);
        }
        return res;
    }

    public static double Len(IEnumerable<Iv> merged) => merged.Sum(i => i.Sec);

    /// <summary>a と、マージ済みの集合 set の重なり(秒)。</summary>
    public static double Overlap(Iv a, IReadOnlyList<Iv> set)
    {
        double sum = 0;
        foreach (var b in set)
        {
            var s = a.S > b.S ? a.S : b.S;
            var e = a.E < b.E ? a.E : b.E;
            if (e > s) sum += (e - s).TotalSeconds;
        }
        return sum;
    }

    /// <summary>a から、マージ済みの集合 set を除いた残りの区間。</summary>
    public static List<Iv> Subtract(Iv a, IReadOnlyList<Iv> set)
    {
        var res = new List<Iv>();
        var cur = a.S;
        foreach (var b in set)
        {
            if (b.E <= cur || b.S >= a.E) continue;
            if (b.S > cur) res.Add(new Iv(cur, b.S));
            if (b.E > cur) cur = b.E;
            if (cur >= a.E) break;
        }
        if (cur < a.E) res.Add(new Iv(cur, a.E));
        return res;
    }

    public static Iv? Clip(Iv a, Iv bounds)
    {
        var s = a.S > bounds.S ? a.S : bounds.S;
        var e = a.E < bounds.E ? a.E : bounds.E;
        return e > s ? new Iv(s, e) : null;
    }

    public static List<Iv> ClipAll(IEnumerable<Iv> src, Iv bounds) =>
        src.Select(i => Clip(i, bounds)).Where(i => i is not null).Select(i => i!.Value).ToList();
}
