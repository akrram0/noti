using System.Runtime.InteropServices;

namespace Noti.App.Interop;

/// <summary>
/// Consolidated Win32 P/Invoke declarations used across the application.
/// </summary>
internal static class NativeMethods
{
    // Window messages
    internal const uint WM_SYSCOMMAND    = 0x0112;
    internal const uint WM_NCLBUTTONDOWN = 0x00A1;

    // System command & hit-test parameters
    internal const int SC_MOVE   = 0xF010;
    internal const int HTCAPTION = 0x0002;
    internal const int VK_LBUTTON = 0x01;

    /// <summary>Releases mouse capture from the calling thread's window.</summary>
    [DllImport("user32.dll")]
    internal static extern bool ReleaseCapture();

    /// <summary>Sends a message to a window or windows.</summary>
    [DllImport("user32.dll")]
    internal static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Checks physical mouse/key button state at the OS hardware level.</summary>
    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);
}
