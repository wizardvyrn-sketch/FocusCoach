using System.Threading.Channels;

namespace FocusCoach.Core;

/// <summary>画面に渡す軽量な状態(UIは Core の状態を直接読まず、これを購読する。07章 §7.2)。</summary>
public sealed record StateSnapshot(
    RunStatus Status, string SubtaskName, int SubtaskNumber, int SubtaskCount,
    double ElapsedSec, double SubtaskSec, double? SubtaskTargetSec, double? TargetSec,
    PanelEdge Edge, string EdgeReason, bool Paused, DateTimeOffset At,
    string FirstStep)
{
    public static StateSnapshot From(RunState s, DateTimeOffset now)
    {
        var m = MetricsCalculator.Compute(s, now);
        var a = s.Active;
        var sm = a is null ? null : m.Subtasks.FirstOrDefault(x => x.Index == a.Index);
        var edge = Signals.Evaluate(s, now);
        // 着手の一手は、最初のサブタスクの間だけ出す(05 F-02、I-05)
        var first = a is { Index: 0 } && s.Subtasks[0].Status == SubtaskStatus.Active ? s.FirstStep : "";
        return new StateSnapshot(s.Status, a?.Name ?? "", (a?.Index ?? -1) + 1, s.Subtasks.Count,
            m.TotalSec - m.PausedSec, sm?.DurationSec ?? 0, a?.TargetSec, s.TargetSec, edge.Edge, edge.Reason, s.IsPaused, now, first);
    }
}

/// <summary>
/// 直列に処理するイベントループ(07章 §7.2)。処理は常に1件ずつで、同時には走らない(同じ OS スレッドで走ることまでは保証しない)。OS監視・UI・タイマーはすべて Post/Send でコマンドを投入し、
/// Session は常にこのループの上でだけ動く。そのため状態に排他制御が要らず、イベントの順序 = 処理順になる。
/// </summary>
public sealed class EventLoop : IAsyncDisposable
{
    private readonly Session _session;
    private readonly IClock _clock;
    private readonly Channel<(CoreCommand Cmd, TaskCompletionSource<IReadOnlyList<CoreEvent>>? Reply)> _ch =
        Channel.CreateUnbounded<(CoreCommand, TaskCompletionSource<IReadOnlyList<CoreEvent>>?)>(
            new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _worker;

    public event Action<StateSnapshot>? SnapshotChanged;
    public event Action<CoreCommand, Exception>? CommandRejected;
    public event Action<IReadOnlyList<CoreEvent>>? EventsEmitted;

    public EventLoop(Session session, IClock clock)
    {
        _session = session;
        _clock = clock;
        _worker = Task.Run(RunAsync);
    }

    public void Post(CoreCommand cmd) => _ch.Writer.TryWrite((cmd, null));

    public Task<IReadOnlyList<CoreEvent>> SendAsync(CoreCommand cmd)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<CoreEvent>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ch.Writer.TryWrite((cmd, tcs));
        return tcs.Task;
    }

    /// <summary>表示更新用のタイマー刻み。コマンドではなく「今の状態を出す」だけ。</summary>
    public void Tick() => _ch.Writer.TryWrite((new CoreCommand("__tick", new Dictionary<string, string>()), null));

    private async Task RunAsync()
    {
        await foreach (var (cmd, reply) in _ch.Reader.ReadAllAsync())
        {
            try
            {
                if (cmd.Type == "__tick")
                {
                    SafeInvoke(SnapshotChanged, StateSnapshot.From(_session.State, _clock.Now));
                    reply?.TrySetResult([]);
                    continue;
                }
                var evs = _session.Dispatch(cmd);
                // コマンドはここで確定。購読側(UIなど)の例外は、コマンドの成否と切り離す(R-35)
                reply?.TrySetResult(evs);
                SafeInvoke(EventsEmitted, evs);
                SafeInvoke(SnapshotChanged, StateSnapshot.From(_session.State, cmd.At ?? _clock.Now));
            }
            catch (Exception ex)
            {
                // 不正なコマンドでループを止めない。UIには CommandRejected で伝える。
                CommandRejected?.Invoke(cmd, ex);
                reply?.TrySetException(ex);
            }
        }
    }

    /// <summary>購読者の例外。ループは止めない。</summary>
    public event Action<Exception>? SubscriberFailed;

    private void SafeInvoke<T>(Action<T>? handler, T arg)
    {
        if (handler is null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try { ((Action<T>)d)(arg); }
            catch (Exception ex) { try { SubscriberFailed?.Invoke(ex); } catch { /* 通知側の例外は無視 */ } }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _ch.Writer.TryComplete();
        await _worker;
    }
}
