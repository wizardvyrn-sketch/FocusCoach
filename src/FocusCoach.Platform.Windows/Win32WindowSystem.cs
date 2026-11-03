using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FocusCoach.Core;
using FocusCoach.Core.Workspace;
using static FocusCoach.Platform.Windows.NativeMethods;

namespace FocusCoach.Platform.Windows;

/// <summary>
/// IWindowSystem の Win32 実装。
/// 【要ローカル実機確認】クラウドセッション(Linux)ではコンパイルのみ。除外判定(ツール/所有/クローク)と
/// placement の復元は、Windows 実機で `fc workspace --dry-run` 相当の確認をしてから有効化する(docs/plan.md)。
/// </summary>
public sealed class Win32WindowSystem : IWindowSystem
{
    public static IntPtr ToHandle(string hwnd) =>
        new(Convert.ToInt64(hwnd.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hwnd[2..] : hwnd, 16));

    public static string FromHandle(IntPtr h) => "0x" + h.ToInt64().ToString("X");

    public IReadOnlyList<WindowInfo> Enumerate()
    {
        var list = new List<WindowInfo>();
        EnumWindows((h, _) =>
        {
            try { list.Add(Describe(h)); } catch { /* 列挙中に消えたウィンドウは無視 */ }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static WindowInfo Describe(IntPtr h)
    {
        var len = GetWindowTextLength(h);
        var sb = new StringBuilder(len + 1);
        _ = GetWindowText(h, sb, sb.Capacity);
        GetWindowThreadProcessId(h, out var pid);
        var process = "?";
        try { process = Process.GetProcessById((int)pid).ProcessName + ".exe"; } catch { }
        var ex = GetWindowLong(h, GWL_EXSTYLE);
        var cloaked = DwmGetWindowAttribute(h, DWMWA_CLOAKED, out var c, sizeof(int)) == 0 && c != 0;
        return new WindowInfo(
            FromHandle(h), process, sb.ToString(),
            Visible: IsWindowVisible(h), Minimized: IsIconic(h),
            ToolWindow: (ex & WS_EX_TOOLWINDOW) != 0, Owned: GetWindow(h, GW_OWNER) != IntPtr.Zero,
            Cloaked: cloaked, Placement: PlacementOf(h));
    }

    /// <summary>WINDOWPLACEMENT をバイト列の16進で保存する(平坦な文字列として JSONL/DB に載せるため)。</summary>
    private static string? PlacementOf(IntPtr h)
    {
        var p = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(h, ref p)) return null;
        var buf = new byte[p.length];
        var ptr = Marshal.AllocHGlobal(p.length);
        try { Marshal.StructureToPtr(p, ptr, false); Marshal.Copy(ptr, buf, 0, p.length); }
        finally { Marshal.FreeHGlobal(ptr); }
        return Convert.ToHexString(buf);
    }

    public bool Minimize(string hwnd)
    {
        var h = ToHandle(hwnd);
        // フォーカスを奪わない最小化(SW_SHOWMINNOACTIVE)
        return IsWindow(h) && ShowWindow(h, SW_SHOWMINNOACTIVE);
    }

    public bool Restore(string hwnd, string? placement)
    {
        var h = ToHandle(hwnd);
        if (!IsWindow(h)) return false;
        if (placement is null) return ShowWindow(h, SW_RESTORE);
        var bytes = Convert.FromHexString(placement);
        var ptr = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            var p = Marshal.PtrToStructure<WINDOWPLACEMENT>(ptr);
            return SetWindowPlacement(h, ref p);
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    /// <summary>WM_CLOSE を送るだけ。保存確認ダイアログが出たら利用者の操作に任せる(F-10)。</summary>
    public bool RequestClose(string hwnd)
    {
        var h = ToHandle(hwnd);
        return IsWindow(h) && PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }
}
