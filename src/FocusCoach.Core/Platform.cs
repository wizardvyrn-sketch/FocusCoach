using FocusCoach.Core.Workspace;

namespace FocusCoach.Core;

/// <summary>
/// OS依存の窓口(08章 §8.1-2)。Core は定義だけを持ち、実装は Platform.Windows が提供する。
/// 閉じる処理は WM_CLOSE 相当だけ(強制終了は用意しない。CLAUDE.md、spec F-10)。
/// </summary>
public interface IWindowSystem
{
    IReadOnlyList<WindowInfo> Enumerate();
    bool Minimize(string hwnd);
    /// <summary>placement は Enumerate で得た値(null なら通常表示に戻す)。</summary>
    bool Restore(string hwnd, string? placement);
    /// <summary>WM_CLOSE を送る。ウィンドウが閉じたかどうかは別途確認する。</summary>
    bool RequestClose(string hwnd);
}

/// <summary>前面ウィンドウ・アイドルなど、OSの活動を CoreCommand として流し込む監視源。</summary>
public interface IActivitySource : IDisposable
{
    /// <summary>監視を開始する。コマンドは sink を通して EventLoop へ渡す(UIスレッドを経由しない)。</summary>
    void Start(Action<CoreCommand> sink, ParamRegistry p);
    void Stop();
}

public sealed record WsResult(WsStep Step, bool Done, string Detail);

/// <summary>
/// WorkspacePlanner の計画を IWindowSystem で実行する。dryRun=true なら何も動かさず、結果にだけ記録する
/// (08章 §8.1-4: 不可逆な操作にはドライランを用意する)。
/// </summary>
public static class WorkspaceExecutor
{
    public static IReadOnlyList<WsResult> Execute(IEnumerable<WsStep> steps, IWindowSystem ws, bool dryRun)
    {
        var res = new List<WsResult>();
        foreach (var st in steps)
        {
            if (st.Action == WsAction.Keep) { res.Add(new WsResult(st, true, "skip")); continue; }
            if (dryRun) { res.Add(new WsResult(st, true, "dry-run")); continue; }
            try
            {
                var ok = st.Action switch
                {
                    WsAction.Minimize => ws.Minimize(st.Window.Hwnd),
                    WsAction.Restore => ws.Restore(st.Window.Hwnd, st.Window.Placement),
                    WsAction.Close => ws.RequestClose(st.Window.Hwnd),
                    _ => false,
                };
                res.Add(new WsResult(st, ok, ok ? "ok" : "failed"));
            }
            catch (Exception ex)
            {
                // 1つのウィンドウの失敗で全体を止めない(08章 §8.7)
                res.Add(new WsResult(st, false, "error: " + ex.GetType().Name));
            }
        }
        return res;
    }
}

/// <summary>テスト・CLI用の偽のウィンドウシステム。操作を記録するだけ。</summary>
public sealed class FakeWindowSystem(IEnumerable<WindowInfo> windows) : IWindowSystem
{
    private readonly List<WindowInfo> _w = windows.ToList();
    public List<string> Log { get; } = [];
    public HashSet<string> FailOn { get; } = [];

    public IReadOnlyList<WindowInfo> Enumerate() => _w.ToList();

    public bool Minimize(string hwnd) => Do("minimize", hwnd, w => w with { Minimized = true });
    public bool Restore(string hwnd, string? placement) => Do("restore", hwnd, w => w with { Minimized = false });
    public bool RequestClose(string hwnd) => Do("close", hwnd, w => w with { Visible = false });

    private bool Do(string op, string hwnd, Func<WindowInfo, WindowInfo> f)
    {
        Log.Add($"{op}:{hwnd}");
        var i = _w.FindIndex(w => w.Hwnd == hwnd);
        if (i < 0 || FailOn.Contains(hwnd)) return false;
        _w[i] = f(_w[i]);
        return true;
    }
}
