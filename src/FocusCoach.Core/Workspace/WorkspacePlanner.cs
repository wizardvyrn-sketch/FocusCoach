namespace FocusCoach.Core.Workspace;

/// <summary>
/// ウィンドウの列挙結果(Platform が埋める)。判定ロジックは Core に置き、列挙結果のJSONがあれば Linux でも検証できる
/// (07章 §7.5、08章 §8.2 workspace --dry-run)。
/// </summary>
public sealed record WindowInfo(
    string Hwnd, string Process, string Title,
    bool Visible = true, bool Minimized = false, bool ToolWindow = false, bool Owned = false, bool Cloaked = false,
    string? Placement = null);

public enum WsAction { Keep, Minimize, Restore, Close }

public sealed record WsStep(WindowInfo Window, WsAction Action, string Reason);

public static class WorkspacePlanner
{
    /// <summary>[仮案-11] 常に触らないプロセス(シェル、FocusCoach 自身)。</summary>
    private static readonly HashSet<string> NeverTouch = ["focuscoach", "textinputhost", "shellexperiencehost", "searchhost", "startmenuexperiencehost"];

    /// <summary>最小化・復元の対象になりうるトップレベルウィンドウか。ツールウィンドウ、所有ウィンドウ、非表示、クローク状態は除く。</summary>
    public static bool IsCandidate(WindowInfo w) =>
        w.Visible && !w.ToolWindow && !w.Owned && !w.Cloaked &&
        w.Title.Length > 0 && !NeverTouch.Contains(TaskWindowClassifier.NormalizeProcess(w.Process));

    /// <summary>タスク開始時: 候補をすべて最小化する(F-03)。keepProcesses はユーザーが「残す」と指定したアプリ。</summary>
    public static IReadOnlyList<WsStep> PlanStart(IEnumerable<WindowInfo> current, IEnumerable<string>? keepProcesses = null)
    {
        var keep = (keepProcesses ?? []).Select(TaskWindowClassifier.NormalizeProcess).ToHashSet();
        return current.Select(w =>
        {
            if (!IsCandidate(w)) return new WsStep(w, WsAction.Keep, "not a candidate");
            if (keep.Contains(TaskWindowClassifier.NormalizeProcess(w.Process))) return new WsStep(w, WsAction.Keep, "user keep list");
            if (w.Minimized) return new WsStep(w, WsAction.Keep, "already minimized");
            return new WsStep(w, WsAction.Minimize, "minimize at start");
        }).ToList();
    }

    /// <summary>
    /// 中断からの復帰(09章 §9.4.2 [作業画面に戻す]): スナップショットにあった作業ウィンドウを戻し、
    /// それ以外(中断中に開いたもの)は最小化する。閉じない(U-09-2)。
    /// </summary>
    public static IReadOnlyList<WsStep> PlanResume(IReadOnlyList<WindowInfo> workSnapshot, IEnumerable<WindowInfo> current)
    {
        var snapByHwnd = workSnapshot.ToDictionary(w => w.Hwnd);
        var steps = new List<WsStep>();
        foreach (var w in current)
        {
            if (!IsCandidate(w)) steps.Add(new WsStep(w, WsAction.Keep, "not a candidate"));
            else if (snapByHwnd.ContainsKey(w.Hwnd)) steps.Add(new WsStep(w, WsAction.Restore, "work window"));
            else if (!w.Minimized) steps.Add(new WsStep(w, WsAction.Minimize, "opened during interrupt"));
            else steps.Add(new WsStep(w, WsAction.Keep, "already minimized"));
        }
        return steps;
    }

    /// <summary>
    /// タスク終了時(F-10): opened_during のうち、成果物らしいものを除いて閉じる候補にする(S-06 の初期チェック)。
    /// [仮案-12] 成果物判定: タイトルにプロジェクト名を含む Word/Excel/PowerPoint、または最後にアクティブだった文書。
    /// 閉じる処理は WM_CLOSE のみ(強制終了しない)。ここは「候補」を返すだけ。
    /// </summary>
    public static IReadOnlyList<WsStep> PlanFinish(IEnumerable<WindowInfo> openedDuring, string project, string? lastActiveHwnd)
    {
        var office = new HashSet<string> { "winword", "excel", "powerpnt" };
        return openedDuring.Select(w =>
        {
            if (!IsCandidate(w)) return new WsStep(w, WsAction.Keep, "not a candidate");
            var isOffice = office.Contains(TaskWindowClassifier.NormalizeProcess(w.Process));
            var deliverable = (isOffice && project.Length > 0 && w.Title.Contains(project, StringComparison.OrdinalIgnoreCase))
                              || (isOffice && w.Hwnd == lastActiveHwnd);
            return deliverable
                ? new WsStep(w, WsAction.Keep, "deliverable (default unchecked)")
                : new WsStep(w, WsAction.Close, "opened during task");
        }).ToList();
    }
}
