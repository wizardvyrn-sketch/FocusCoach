namespace FocusCoach.Core;

/// <summary>
/// Windows のウィンドウメッセージ(セッション変更・電源)を、離席のコマンドに変換する表(F-15、09章 §9.4.1a、§9.6a)。
/// メッセージの番号は Win32 の定数だが、判定は純粋関数なので Core に置き、Linux でもテストする。
/// 呼び出し側(App のウィンドウプロシージャ)は、戻り値を EventLoop に Post するだけにする。
/// </summary>
public static class WindowsSessionEvents
{
    public const int WM_WTSSESSION_CHANGE = 0x02B1;
    public const int WM_POWERBROADCAST = 0x0218;

    private const int WTS_CONSOLE_CONNECT = 0x1, WTS_CONSOLE_DISCONNECT = 0x2, WTS_REMOTE_CONNECT = 0x3, WTS_REMOTE_DISCONNECT = 0x4;
    private const int WTS_SESSION_LOCK = 0x7, WTS_SESSION_UNLOCK = 0x8;
    private const int PBT_APMSUSPEND = 0x4, PBT_APMRESUMESUSPEND = 0x7, PBT_APMRESUMEAUTOMATIC = 0x12;

    public static CoreCommand? Translate(int msg, long wParam)
    {
        (string Type, string Reason)? r = (msg, wParam) switch
        {
            (WM_WTSSESSION_CHANGE, WTS_SESSION_LOCK) => (Cmd.AwayStart, "lock"),
            (WM_WTSSESSION_CHANGE, WTS_SESSION_UNLOCK) => (Cmd.AwayEnd, "lock"),
            // リモートデスクトップ(外から業務PCへ接続する使い方)の切断・再接続。コンソールの切り替えも同じに扱う
            (WM_WTSSESSION_CHANGE, WTS_REMOTE_DISCONNECT or WTS_CONSOLE_DISCONNECT) => (Cmd.AwayStart, "remote"),
            (WM_WTSSESSION_CHANGE, WTS_REMOTE_CONNECT or WTS_CONSOLE_CONNECT) => (Cmd.AwayEnd, "remote"),
            (WM_POWERBROADCAST, PBT_APMSUSPEND) => (Cmd.AwayStart, "sleep"),
            // 復帰は2種類届くことがある(自動復帰 → 利用者の操作による復帰)。away_end は2回目が来ても何もしない
            (WM_POWERBROADCAST, PBT_APMRESUMEAUTOMATIC or PBT_APMRESUMESUSPEND) => (Cmd.AwayEnd, "sleep"),
            _ => null,
        };
        return r is { } x ? CoreCommand.Of(x.Type, ("reason", x.Reason), ("origin", "monitor")) : null;
    }
}
