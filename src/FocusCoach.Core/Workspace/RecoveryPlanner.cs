using System.Text.Json;

namespace FocusCoach.Core.Workspace;

/// <summary>異常終了からの復帰で、最小化したまま残ったウィンドウを元に戻す計画(F-16 の 4)。</summary>
public static class RecoveryPlanner
{
    private static readonly JsonSerializerOptions Opt = new() { PropertyNameCaseInsensitive = true };

    public static string Serialize(IEnumerable<WindowInfo> windows) => JsonSerializer.Serialize(windows.ToList(), Opt);

    public static List<WindowInfo> Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<List<WindowInfo>>(json, Opt) ?? []; }
        catch (JsonException) { return []; }
    }

    /// <summary>
    /// 開始時のスナップショットにあり、いまも存在して最小化されているウィンドウを「元の配置に戻す」計画。
    /// Windows を再起動した後(boot_id が違う)は hwnd が無効なので、何もしない。
    /// 最小化以外のウィンドウ、スナップショットに無いウィンドウには触れない。
    /// </summary>
    public static IReadOnlyList<WsStep> PlanRestoreAfterCrash(RunState s, string currentBootId, IEnumerable<WindowInfo> current)
    {
        if (!s.WsSnapshots.TryGetValue("start", out var snap) || snap.BootId.Length == 0 || snap.BootId != currentBootId) return [];
        var before = Deserialize(snap.WindowsJson).ToDictionary(w => w.Hwnd);
        return current
            .Where(w => before.TryGetValue(w.Hwnd, out var b) && w.Minimized && !b.Minimized && WorkspacePlanner.IsCandidate(b with { Minimized = false }))
            .Select(w => new WsStep(w with { Placement = before[w.Hwnd].Placement }, WsAction.Restore, "minimized by FocusCoach before crash"))
            .ToList();
    }
}
