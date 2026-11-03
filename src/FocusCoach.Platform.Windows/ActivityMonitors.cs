using System.Globalization;
using System.Runtime.InteropServices;
using FocusCoach.Core;
using FocusCoach.Core.Workspace;
using static FocusCoach.Platform.Windows.NativeMethods;

namespace FocusCoach.Platform.Windows;

/// <summary>監視の共通部分: 処理の重なり防止、連続失敗の検知(08章 §8.7、R-27、R-29)。</summary>
public abstract class MonitorBase : IActivitySource
{
    private Timer? _timer;
    private int _busy;
    private int _failures;
    protected Action<CoreCommand>? Sink;

    public abstract string Name { get; }
    /// <summary>連続失敗がしきい値を超えて、監視を止めたときに通知する。</summary>
    public event Action<string, Exception>? Faulted;
    public bool IsFaulted { get; private set; }
    public const int MaxConsecutiveFailures = 5;

    public void Start(Action<CoreCommand> sink, ParamRegistry p)
    {
        Stop();
        Sink = sink;
        _failures = 0;
        IsFaulted = false;
        Reset(p);   // 実行ごとに内部状態を作り直す(R-34)
        _timer = new Timer(_ => Tick(), null, 0, PeriodMs(p));
    }

    public void Stop() { _timer?.Dispose(); _timer = null; }

    protected abstract void Reset(ParamRegistry p);
    protected abstract int PeriodMs(ParamRegistry p);
    protected abstract void Poll();

    /// <summary>監視由来のコマンド。時刻の巻き戻りは Core が切り上げる(origin=monitor)。</summary>
    protected void Emit(string type, params (string, string)[] data) =>
        Sink?.Invoke(CoreCommand.Of(type, data.Append(("origin", "monitor")).ToArray()));

    private void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return; // 前回の処理が終わっていなければ飛ばす
        try
        {
            Poll();
            _failures = 0;
        }
        catch (Exception ex)
        {
            if (++_failures >= MaxConsecutiveFailures && !IsFaulted)
            {
                IsFaulted = true;
                Stop();
                Faulted?.Invoke(Name, ex);
            }
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    public void Dispose() => Stop();
}

/// <summary>
/// 前面ウィンドウ・新規ウィンドウ・アイドルのポーリング。
/// [仮案-16] SetWinEventHook ではなくポーリング。プロセス名は pid ごとにキャッシュし、新規ウィンドウだけ詳しく調べる(R-27)。
/// 【要ローカル実機確認】
/// </summary>
public sealed class ActivityMonitor : MonitorBase
{
    private string _lastHwnd = "";
    private string _lastTitle = "";
    private bool _idle;
    private double _idleThresholdSec;
    private readonly HashSet<IntPtr> _known = [];

    public override string Name => "Monitor.Foreground";
    protected override int PeriodMs(ParamRegistry p) => (int)p["foreground_poll_ms"];

    protected override void Reset(ParamRegistry p)
    {
        _lastHwnd = ""; _lastTitle = ""; _idle = false;
        _idleThresholdSec = p.IdleThresholdSec;
        _known.Clear();
        foreach (var h in TopLevelHandles()) _known.Add(h);   // 開始時点で存在するウィンドウは opened_during にしない
    }

    private static List<IntPtr> TopLevelHandles()
    {
        var list = new List<IntPtr>();
        EnumWindows((h, _) => { list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }

    protected override void Poll()
    {
        var fg = GetForegroundWindow();
        if (fg != IntPtr.Zero)
        {
            var w = Win32WindowSystem.Describe(fg);
            // タイトルも比べるのは、ブラウザのタブ切替やファイルを開いたときの変化を拾うため
            if (w.Hwnd != _lastHwnd || w.Title != _lastTitle)
            {
                _lastHwnd = w.Hwnd; _lastTitle = w.Title;
                Emit(Cmd.Focus, ("process", w.Process), ("title", w.Title), ("hwnd", w.Hwnd));
            }
        }

        var handles = TopLevelHandles();
        foreach (var h in handles)
        {
            if (!_known.Add(h)) continue;
            var w = Win32WindowSystem.Describe(h);
            if (WorkspacePlanner.IsCandidate(w))
                Emit(Cmd.WindowOpened, ("hwnd", w.Hwnd), ("process", w.Process), ("title", w.Title));
        }
        _known.IntersectWith(handles); // 閉じたウィンドウは忘れる(hwnd の再利用に備える)

        var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (GetLastInputInfo(ref li))
        {
            var idleMs = (uint)Environment.TickCount - li.dwTime;
            if (!_idle && idleMs / 1000.0 >= _idleThresholdSec)
            {
                _idle = true;
                var lastInput = DateTimeOffset.Now - TimeSpan.FromMilliseconds(idleMs);
                Emit(Cmd.IdleStart, ("last_input_at", lastInput.ToString("O", CultureInfo.InvariantCulture)));
            }
            else if (_idle && idleMs / 1000.0 < _idleThresholdSec)
            {
                _idle = false;
                Emit(Cmd.IdleEnd);
            }
        }
    }
}

/// <summary>
/// 開いているフォルダ(Explorer COM)の監視(F-06 MVP)。
/// 0.4A は動いていなかった(実機ログ R-42)。原因の候補だった dynamic の foreach と MTA からの呼び出しをやめ、
/// 専用の STA スレッドで Count / Item(i) により列挙し、COM オブジェクトを解放する(R-28)。
/// 【要ローカル実機確認】L-23
/// </summary>
public sealed class ExplorerFolderMonitor : IActivitySource
{
    private Thread? _thread;
    private volatile bool _running;
    private Action<CoreCommand>? _sink;
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private int _failures;

    public string Name => "Monitor.Explorer";
    public event Action<string, Exception>? Faulted;

    public void Start(Action<CoreCommand> sink, ParamRegistry p)
    {
        Stop();
        _sink = sink;
        _seen.Clear();   // 実行ごとにリセット(0.4A では2回目の実行で記録されなかった。R-34)
        _failures = 0;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "fc-explorer-sta" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(TimeSpan.FromSeconds(3));
        _thread = null;
    }

    private void Loop()
    {
        var t = Type.GetTypeFromProgID("Shell.Application");
        if (t is null) { Faulted?.Invoke(Name, new InvalidOperationException("Shell.Application is not available")); return; }
        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(t);
            while (_running)
            {
                try { PollOnce(shell!); _failures = 0; }
                catch (Exception ex)
                {
                    if (++_failures >= MonitorBase.MaxConsecutiveFailures) { Faulted?.Invoke(Name, ex); return; }
                }
                for (var i = 0; i < 20 && _running; i++) Thread.Sleep(100);   // 2秒
            }
        }
        finally { if (shell is not null) Marshal.FinalReleaseComObject(shell); }
    }

    private void PollOnce(object shell)
    {
        dynamic windows = ((dynamic)shell).Windows();
        try
        {
            int count = windows.Count;
            for (var i = 0; i < count; i++)
            {
                object? w = null;
                try
                {
                    w = windows.Item(i);
                    if (w is null) continue;
                    string? path = null;
                    try { path = ((dynamic)w).Document.Folder.Self.Path; } catch { /* IE や読み込み中のウィンドウ */ }
                    if (!string.IsNullOrEmpty(path) && !path.StartsWith("::", StringComparison.Ordinal) && _seen.Add(path))
                        _sink?.Invoke(CoreCommand.Of(Cmd.Resource, ("kind", "folder"), ("location", path), ("source", "explorer"), ("origin", "monitor")));
                }
                finally { if (w is not null && Marshal.IsComObject(w)) Marshal.ReleaseComObject(w); }
            }
        }
        finally { if (Marshal.IsComObject((object)windows)) Marshal.ReleaseComObject((object)windows); }
    }

    public void Dispose() => Stop();
}
