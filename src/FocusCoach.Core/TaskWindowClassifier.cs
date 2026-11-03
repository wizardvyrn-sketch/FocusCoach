namespace FocusCoach.Core;

/// <summary>
/// 「タスクのウィンドウ」の判定(docs/spec/05 F-11 のオンタスク定義)。
/// opened_during のウィンドウ、アクセス記録に一致するもの、ユーザーが指定したアプリ、FocusCoach 自身。
/// 判定結果は focus_changed イベントに書き込む(リプレイで同じ結果になる)。
/// </summary>
public static class TaskWindowClassifier
{
    /// <summary>[仮案-05] 割り込みアプリの既定。09章 §9.6。設定画面で変更できるようにする予定。</summary>
    public static readonly IReadOnlyList<string> DefaultInterruptApps = ["ms-teams", "teams", "outlook", "olk"];

    public const string SelfProcess = "focuscoach";

    public static string NormalizeProcess(string p)
    {
        var n = p.Trim().ToLowerInvariant();
        return n.EndsWith(".exe", StringComparison.Ordinal) ? n[..^4] : n;
    }

    public static bool IsInterruptApp(string process, IEnumerable<string> interruptApps) =>
        interruptApps.Contains(NormalizeProcess(process));

    /// <summary>
    /// 判定の順(式 v2、05 F-11): 自分自身 → アクセス記録に一致 → タスク用アプリ → 割り込みアプリ(オフ)→ 作業中に開いたウィンドウ → その他(オフ)。
    /// 割り込みアプリは、作業中に開いたウィンドウ(Teams のポップアウトチャットなど)でもオンタスクにしない。
    /// </summary>
    public static (bool TaskWindow, string Reason) Classify(
        RunState s, string process, string title, string hwnd,
        IEnumerable<string> taskApps, IEnumerable<string> interruptApps)
    {
        var p = NormalizeProcess(process);
        if (p == SelfProcess) return (true, "self");
        if (title.Length > 0 && s.Resources.Values.Any(r => r.DisplayName.Length >= 3 && title.Contains(r.DisplayName, StringComparison.OrdinalIgnoreCase)))
            return (true, "resource_match");
        if (taskApps.Contains(p) || s.TaskApps.Contains(p)) return (true, "task_app");
        if (interruptApps.Contains(p)) return (false, "interrupt_app");
        if (hwnd.Length > 0 && s.OpenedDuring.Any(w => w.Hwnd == hwnd && w.ClosedAt is null)) return (true, "opened_during");
        return (false, "other");
    }
}
