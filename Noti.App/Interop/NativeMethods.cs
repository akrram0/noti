using System.Runtime.InteropServices;

namespace Noti.App.Interop;

/// <summary>
/// Consolidated Win32 P/Invoke declarations used across the application.
/// </summary>
internal static class NativeMethods
{
    // Window messages
    internal const uint WM_GETMINMAXINFO = 0x0024;
    internal const uint WM_SYSCOMMAND    = 0x0112;
    internal const uint WM_NCLBUTTONDOWN = 0x00A1;

    // System command & hit-test parameters
    internal const int SC_MOVE    = 0xF010;
    internal const int HTCAPTION  = 0x0002;
    internal const int VK_LBUTTON = 0x01;

    internal delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    /// <summary>Releases mouse capture from the calling thread's window.</summary>
    [DllImport("user32.dll")]
    internal static extern bool ReleaseCapture();

    /// <summary>Sends a message to a window or windows.</summary>
    [DllImport("user32.dll")]
    internal static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Checks physical mouse/key button state at the OS hardware level.</summary>
    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    /// <summary>Returns the dots per inch (dpi) value for the specified window.</summary>
    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>Installs or updates a window subclass callback.</summary>
    [DllImport("comctl32.dll", SetLastError = true)]
    internal static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

    /// <summary>Calls the next handler in a window's subclass chain.</summary>
    [DllImport("comctl32.dll", SetLastError = true)]
    internal static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
}
