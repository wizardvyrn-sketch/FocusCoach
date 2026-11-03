using System.Runtime.InteropServices;
using System.Text;

namespace FocusCoach.Platform.Windows;

internal static class NativeMethods
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] public static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT p);
    [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT p);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO p);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);

    public const uint GW_OWNER = 4;
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x80;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TRANSPARENT = 0x20;
    public const int WS_EX_LAYERED = 0x80000;
    public const int DWMWA_CLOAKED = 14;
    public const int SW_SHOWMINNOACTIVE = 7;
    public const int SW_RESTORE = 9;
    public const uint WM_CLOSE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public int length, flags, showCmd;
        public POINT ptMinPosition, ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO { public uint cbSize, dwTime; }
}

/// <summary>App(WPF)から使う公開ラッパー。【要ローカル実機確認】</summary>
public static class NativeMethodsPublic
{
    public const int NoActivate = NativeMethods.WS_EX_NOACTIVATE;
    public const int ToolWindow = NativeMethods.WS_EX_TOOLWINDOW;
    public const int WS_EX_TRANSPARENT = NativeMethods.WS_EX_TRANSPARENT;
    /// <summary>クリックを下のウィンドウに通す(コンパクト表示。WS_EX_TRANSPARENT は WS_EX_LAYERED と組で効く)。</summary>
    public const int ClickThrough = NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED;
    public const int VK_CONTROL = 0x11;

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativeMethods.POINT p);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);

    /// <summary>画面上のマウスの位置(物理ピクセル)。取れなければ null。</summary>
    public static (int X, int Y)? CursorPosition() => GetCursorPos(out var p) ? (p.X, p.Y) : null;

    /// <summary>Ctrl キーが押されているか(どのウィンドウがアクティブでも見られる)。</summary>
    public static bool IsCtrlDown() => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int MA_NOACTIVATE = 3;
    public const int WM_HOTKEY = 0x0312;

    public static int GetExStyle(IntPtr h) => NativeMethods.GetWindowLong(h, NativeMethods.GWL_EXSTYLE);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr h, int index, int value);

    public static void SetExStyle(IntPtr h, int style) => SetWindowLong(h, NativeMethods.GWL_EXSTYLE, style);

    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);
    private const uint WDA_MONITOR = 0x1, WDA_EXCLUDEFROMCAPTURE = 0x11;

    /// <summary>
    /// 画面共有・録画・スクリーンショットに映らないようにする(09章 §9.4.6、R-20)。
    /// WDA_EXCLUDEFROMCAPTURE は Windows 10 2004 以降。失敗したら WDA_MONITOR(黒く映る)にフォールバックし、それも失敗なら false。
    /// 開発用: 環境変数 FOCUSCOACH_ALLOW_CAPTURE=1 のときは除外しない(見た目の確認でスクリーンショットを撮るため。既定は除外する)。
    /// </summary>
    public static bool ExcludeFromCapture(IntPtr h) =>
        Environment.GetEnvironmentVariable("FOCUSCOACH_ALLOW_CAPTURE") == "1"
        || SetWindowDisplayAffinity(h, WDA_EXCLUDEFROMCAPTURE) || SetWindowDisplayAffinity(h, WDA_MONITOR);

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);
    private const uint MOD_NOREPEAT = 0x4000;

    /// <summary>修飾キーなしのグローバルホットキー(F9/F10 など)を登録する。失敗したら false(他のアプリが使用中など)。</summary>
    public static bool RegisterKey(IntPtr h, int id, int vk, uint modifiers = 0) => RegisterHotKey(h, id, MOD_NOREPEAT | modifiers, (uint)vk);
    public static void UnregisterKey(IntPtr h, int id) => UnregisterHotKey(h, id);

    [DllImport("wtsapi32.dll")] private static extern bool WTSRegisterSessionNotification(IntPtr h, int flags);
    [DllImport("wtsapi32.dll")] private static extern bool WTSUnRegisterSessionNotification(IntPtr h);

    /// <summary>ロック・リモート切断などの通知(WM_WTSSESSION_CHANGE)を受け取る(F-15)。</summary>
    public static bool RegisterSessionNotification(IntPtr h) => WTSRegisterSessionNotification(h, 0 /* NOTIFY_FOR_THIS_SESSION */);
    public static void UnregisterSessionNotification(IntPtr h) => WTSUnRegisterSessionNotification(h);

    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out NativeMethods.RECT r);

    /// <summary>ウィンドウの位置と大きさ(物理ピクセル)。最小化中・取れないときは null。</summary>
    public static (int X, int Y, int W, int H)? WindowRect(IntPtr h)
    {
        if (h == IntPtr.Zero || !NativeMethods.IsWindow(h) || NativeMethods.IsIconic(h) || !GetWindowRect(h, out var r)) return null;
        return r.Right > r.Left && r.Bottom > r.Top ? (r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : null;
    }

    /// <summary>最後のキー・マウス入力からの秒数(どのアプリへの入力でも)。取れなければ無限大。</summary>
    public static double SecondsSinceLastInput()
    {
        var info = new NativeMethods.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.LASTINPUTINFO>() };
        if (!NativeMethods.GetLastInputInfo(ref info)) return double.PositiveInfinity;
        return unchecked((uint)Environment.TickCount - info.dwTime) / 1000.0;
    }

    /// <summary>
    /// Windows の起動ごとの識別子(F-16: 異常終了後に、ウィンドウの記録がまだ有効かの判定に使う)。
    /// 起動時刻を分単位に丸めたもの。[仮案-28] 時刻の補正で分の境目をまたぐと別の起動と判定される(その場合は復元を提案しないだけで、害は無い)。
    /// </summary>
    public static string BootId()
    {
        var boot = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
        return boot.ToString("yyyyMMddHHmm");
    }
}
