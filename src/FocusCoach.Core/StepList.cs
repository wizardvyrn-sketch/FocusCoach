namespace FocusCoach.Core;

/// <summary>タスクを選んだときに入れる手順の出どころ(優先順: 登録した手順 → 前回の下書き → 最後に終了した実行)。</summary>
public enum StepSource { Registered, Draft, LastRun }

/// <summary>タスクの手順(サブタスク)の1行。目安は分(未指定は null)。</summary>
public sealed record StepItem(string Name, double? TargetMin);

/// <summary>
/// 手順の一覧の変換と検証(05 F-01・F-02、03 S-02a 手順エディタ)。
/// 開始コマンド(start_task の subtasks)は「名前:分」をカンマで区切った文字列なので、エディタの一覧とこの形式を行き来させる。
/// </summary>
public static class StepList
{
    public const double MaxTargetMin = 600;
    private static readonly char[] Forbidden = [',', '、', '，', ':', '：'];

    /// <summary>start_task に渡す文字列にする(例: 見積確認:25,執筆:40)。</summary>
    public static string Format(IEnumerable<StepItem> steps) =>
        string.Join(",", steps.Select(s => s.TargetMin is { } m
            ? $"{s.Name}:{m.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}"
            : s.Name));

    /// <summary>「名前:分」をカンマで区切った文字列を一覧にする(Reducer.ParseSubtaskList と同じ規則)。</summary>
    public static List<StepItem> Parse(string spec) =>
        Reducer.ParseSubtaskList(spec).Select(p => new StepItem(p.Name, p.TargetSec / 60)).ToList();

    /// <summary>画面に出す短い要約(例: 見積確認 25分 → 執筆 40分)。</summary>
    public static string Summary(IReadOnlyList<StepItem> steps) =>
        steps.Count == 0 ? Loc.T("steps.summary.none") : string.Join("  →  ", steps.Select(s => s.TargetMin is { } m ? Loc.T("steps.summary.item", s.Name, m) : s.Name));

    /// <summary>
    /// エディタの入力を整える: 前後の空白を取り、名前が空の行は捨てる。
    /// 問題があればエラー文の一覧を返す(空なら OK)。名前の重複、使えない文字、目安の範囲を調べる。
    /// </summary>
    public static (List<StepItem> Steps, List<string> Errors) Normalize(IEnumerable<(string Name, string Minutes)> rows)
    {
        var steps = new List<StepItem>();
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var n = 0;
        foreach (var (rawName, rawMin) in rows)
        {
            n++;
            var name = (rawName ?? "").Trim();
            var min = (rawMin ?? "").Trim();
            if (name.Length == 0)
            {
                if (min.Length > 0) errors.Add(Loc.T("steps.error.name", n));
                continue;
            }
            if (name.IndexOfAny(Forbidden) >= 0) errors.Add(Loc.T("steps.error.forbidden", n));
            if (!seen.Add(name)) errors.Add(Loc.T("steps.error.dup", n, name));
            double? target = null;
            if (min.Length > 0)
            {
                if (double.TryParse(min, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 && v <= MaxTargetMin)
                    target = v;
                else
                    errors.Add(Loc.T("steps.error.target", n, MaxTargetMin));
            }
            steps.Add(new StepItem(name, target));
        }
        if (steps.Count == 0 && errors.Count == 0) errors.Add(Loc.T("steps.error.empty"));
        return (steps, errors);
    }
}
