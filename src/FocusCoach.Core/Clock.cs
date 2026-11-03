namespace FocusCoach.Core;

/// <summary>時刻はすべて IClock 経由で得る(08章 §8.1: 時刻も入力の一部)。</summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}

/// <summary>テスト・シナリオ用。進める操作は呼び出し側が行う。</summary>
public sealed class ManualClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset Now { get; set; } = start;
}
