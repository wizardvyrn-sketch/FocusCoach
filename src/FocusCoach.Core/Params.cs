using System.Globalization;

namespace FocusCoach.Core;

public sealed record ParamDef(string Name, double Default, double Min, double Max, string Description);

/// <summary>
/// すべてのしきい値と係数の登録簿(07章 §7.4、08章 §8.6)。
/// 設定画面・デバッグパネル・シナリオDSL(set)のいずれも同じ名前で変更する。
/// </summary>
public sealed class ParamRegistry
{
    public static readonly IReadOnlyList<ParamDef> Definitions =
    [
        new("idle_threshold_sec", 120, 30, 1800, "No input for at least this many seconds counts as idle (the whole span is counted)"),
        new("guide_history_n", 5, 1, 50, "Number of recent runs used for the guide and comparison"),
        new("target_ratio", 0.95, 0.5, 1.5, "Target time = past median x this factor"),
        new("focus_switch_penalty", 2, 0, 10, "Focus score penalty per app switch per hour"),
        new("focus_switch_penalty_max", 20, 0, 100, "Upper limit of the switch penalty"),
        new("foreground_poll_ms", 2000, 250, 10000, "Foreground window polling interval"),
        new("timeline_bucket_sec", 300, 60, 1800, "Bucket size of the focus timeline"),
        new("interrupt_app_dwell_sec", 90, 10, 600, "Seconds in an interruption app before it counts as dwelling there (spec 09 9.4.4, U-09-6)"),
        new("resume_card_auto_close_sec", 60, 5, 600, "Seconds before the resume card collapses on its own (spec 09 9.4.2)"),
        new("improve_min_delta_sec", 120, 0, 3600, "Improvement points: ignore subtask time differences smaller than this"),
        new("search_long_sec", 300, 30, 3600, "Improvement points: suggest when document search time is at least this long"),
        new("away_prompt_sec", 300, 60, 7200, "Ask whether to record it as an interruption after being away at least this long (spec 09 9.4.1a)"),
        new("score_full_weight_sec", 600, 60, 7200, "When active time is below this, prorate the switch penalty (short-run safeguard; formula v2)"),
    ];

    private readonly Dictionary<string, double> _values =
        Definitions.ToDictionary(d => d.Name, d => d.Default);

    public double this[string name] =>
        _values.TryGetValue(name, out var v) ? v : throw new KeyNotFoundException($"unknown param: {name}");

    public IReadOnlyDictionary<string, double> All => _values;

    public double IdleThresholdSec => this["idle_threshold_sec"];

    public bool TrySet(string name, double value, out double old, out string? error)
    {
        old = 0;
        error = null;
        var def = Definitions.FirstOrDefault(d => d.Name == name);
        if (def is null) { error = $"unknown param: {name}"; return false; }
        if (value < def.Min || value > def.Max) { error = $"{name}={value} is out of range [{def.Min}, {def.Max}]"; return false; }
        old = _values[name];
        _values[name] = value;
        return true;
    }

    public bool TrySet(string name, string value, out double old, out string? error)
    {
        old = 0;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            error = $"{name}: not a number: {value}";
            return false;
        }
        return TrySet(name, d, out old, out error);
    }

    public ParamRegistry Clone()
    {
        var c = new ParamRegistry();
        foreach (var (k, v) in _values) c._values[k] = v;
        return c;
    }
}
