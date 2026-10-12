using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Noti.App.Interop;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;

namespace Noti.App.Services;

/// <summary>
/// Manages window chrome configuration (icon, size, minimum bounds, title bar regions, and click-and-hold drag behaviour)
/// for a single WinUI 3 Window.
/// </summary>
internal sealed class WindowChromeService : IDisposable
{
    public const int DefaultMinWidth  = 520;
    public const int DefaultMinHeight = 450;

    private readonly Window _window;
    private readonly IntPtr _hwnd;
    private readonly NativeMethods.SUBCLASSPROC _subclassProc;
    private int _minWidth  = DefaultMinWidth;
    private int _minHeight = DefaultMinHeight;
    private RectInt32 _lastCaptionRect;
    private RectInt32[] _lastPassthroughRects = [];
    private bool _disposed;

    public WindowChromeService(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window       = window;
        _hwnd         = WindowNative.GetWindowHandle(window);
        _subclassProc = WindowSubclassProc;
    }

    /// <summary>
    /// Configures the initial window chrome: icon, size, minimum window bounds, and custom title bar.
    /// Call once from the Window constructor after InitializeComponent().
    /// </summary>
    public void Configure(int width = 1080, int height = 680, int minWidth = DefaultMinWidth, int minHeight = DefaultMinHeight)
    {
        _minWidth  = minWidth;
        _minHeight = minHeight;

        NativeMethods.SetWindowSubclass(_hwnd, _subclassProc, (UIntPtr)1, IntPtr.Zero);
        _window.AppWindow.Resize(new SizeInt32(Math.Max(width, minWidth), Math.Max(height, minHeight)));

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Noti.ico");
        if (File.Exists(iconPath))
            _window.AppWindow.SetIcon(iconPath);

        _window.ExtendsContentIntoTitleBar = true;

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = _window.AppWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar     = true;
            titleBar.PreferredHeightOption          = TitleBarHeightOption.Tall;
            titleBar.ButtonBackgroundColor          = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor  = Colors.Transparent;
        }
    }

    private IntPtr WindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == NativeMethods.WM_GETMINMAXINFO && lParam != IntPtr.Zero)
        {
            var dpi   = NativeMethods.GetDpiForWindow(hWnd);
            var scale = dpi > 0 ? dpi / 96.0 : 1.0;
            var info  = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);
            info.ptMinTrackSize.X = (int)Math.Ceiling(_minWidth * scale);
            info.ptMinTrackSize.Y = (int)Math.Ceiling(_minHeight * scale);
            Marshal.StructureToPtr(info, lParam, false);
            return IntPtr.Zero;
        }

        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    /// <summary>
    /// Synchronizes native non-client Caption (for genuine OS click-and-hold window dragging)
    /// and Passthrough regions (for interactive XAML controls) after layout completes.
    /// </summary>
    public void UpdateTitleBarRegions(FrameworkElement titleBarContainer, params FrameworkElement[] interactiveControls)
    {
        if (_disposed || !AppWindowTitleBar.IsCustomizationSupported() || titleBarContainer.XamlRoot is null)
            return;

        var scale = titleBarContainer.XamlRoot.RasterizationScale;
        if (scale <= 0 || titleBarContainer.ActualWidth <= 0)
            return;

        var titleBar = _window.AppWindow.TitleBar;
        var rightCaptionInset = titleBar.RightInset > 0
            ? titleBar.RightInset
            : (int)Math.Ceiling(140 * scale);

        var totalWidthPx  = (int)Math.Round(titleBarContainer.ActualWidth * scale);
        var totalHeightPx = (int)Math.Round(48 * scale);
        var usableWidthPx = Math.Max(0, totalWidthPx - rightCaptionInset);
        var captionRect   = new RectInt32(0, 0, usableWidthPx, totalHeightPx);

        var passthroughList = new List<RectInt32>(interactiveControls.Length);
        foreach (var ctrl in interactiveControls)
        {
            if (ctrl is null || ctrl.Visibility != Visibility.Visible || ctrl.ActualWidth <= 0 || ctrl.ActualHeight <= 0)
                continue;

            try
            {
                var bounds = ctrl.TransformToVisual(titleBarContainer)
                                 .TransformBounds(new Rect(0, 0, ctrl.ActualWidth, ctrl.ActualHeight));

                var x = Math.Max(0, (int)Math.Floor((bounds.X - 2) * scale));
                var y = Math.Max(0, (int)Math.Floor((bounds.Y - 2) * scale));
                var w = (int)Math.Ceiling((bounds.Width + 4) * scale);
                var h = (int)Math.Ceiling((bounds.Height + 4) * scale);

                if (w > 0 && h > 0)
                    passthroughList.Add(new RectInt32(x, y, w, h));
            }
            catch
            {
            }
        }

        if (AreRegionsEqual(captionRect, passthroughList))
            return;

        _lastCaptionRect      = captionRect;
        _lastPassthroughRects = passthroughList.ToArray();

        try
        {
            var nonClientSource = InputNonClientPointerSource.GetForWindowId(_window.AppWindow.Id);
            if (usableWidthPx > 0 && totalHeightPx > 0)
                nonClientSource.SetRegionRects(NonClientRegionKind.Caption, [captionRect]);
            else
                nonClientSource.ClearRegionRects(NonClientRegionKind.Caption);

            if (_lastPassthroughRects.Length > 0)
                nonClientSource.SetRegionRects(NonClientRegionKind.Passthrough, _lastPassthroughRects);
            else
                nonClientSource.ClearRegionRects(NonClientRegionKind.Passthrough);
        }
        catch
        {
        }
    }

    private bool AreRegionsEqual(RectInt32 captionRect, List<RectInt32> passthroughList)
    {
        if (_lastCaptionRect.X != captionRect.X ||
            _lastCaptionRect.Y != captionRect.Y ||
            _lastCaptionRect.Width != captionRect.Width ||
            _lastCaptionRect.Height != captionRect.Height ||
            _lastPassthroughRects.Length != passthroughList.Count)
        {
            return false;
        }

        for (var i = 0; i < passthroughList.Count; i++)
        {
            var a = _lastPassthroughRects[i];
            var b = passthroughList[i];
            if (a.X != b.X || a.Y != b.Y || a.Width != b.Width || a.Height != b.Height)
                return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            NativeMethods.RemoveWindowSubclass(_hwnd, _subclassProc, (UIntPtr)1);
        }
        catch
        {
        }
    }
}

