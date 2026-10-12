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
internal sealed class WindowChromeService
{
    public const int DefaultMinWidth  = 520;
    public const int DefaultMinHeight = 450;

    private readonly Window _window;
    private readonly IntPtr _hwnd;
    private readonly NativeMethods.SUBCLASSPROC _subclassProc;
    private int _minWidth  = DefaultMinWidth;
    private int _minHeight = DefaultMinHeight;
    private bool _isDragCandidate;
    private Point _dragStartPoint;

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
    /// Marks the custom title bar area (left of the native Min/Max/Close caption buttons) as Passthrough
    /// so all XAML controls (Update button, Zoom badge, Mode switcher, Sidebar toggle) receive direct hover/click
    /// events at any window size while AppTitleBar handles 4px-threshold click-and-hold DWM dragging.
    /// </summary>
    public void UpdateTitleBarRegions(FrameworkElement titleBarContainer, params FrameworkElement[] interactiveControls)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported() || titleBarContainer.XamlRoot is null)
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

        try
        {
            var nonClientSource = InputNonClientPointerSource.GetForWindowId(_window.AppWindow.Id);
            nonClientSource.ClearRegionRects(NonClientRegionKind.Caption);

            if (usableWidthPx > 0 && totalHeightPx > 0)
            {
                var clientTitleRect = new RectInt32(0, 0, usableWidthPx, totalHeightPx);
                nonClientSource.SetRegionRects(NonClientRegionKind.Passthrough, [clientTitleRect]);
            }
            else
            {
                nonClientSource.ClearRegionRects(NonClientRegionKind.Passthrough);
            }
        }
        catch
        {
        }
    }

    public void OnDragSurfacePointerPressed(UIElement sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(sender);
        if (pt.Properties.IsLeftButtonPressed)
        {
            _isDragCandidate = true;
            _dragStartPoint  = pt.Position;
        }
        else
        {
            _isDragCandidate = false;
        }
    }

    public void OnDragSurfacePointerMoved(UIElement sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isDragCandidate)
            return;

        var pt = e.GetCurrentPoint(sender);
        var isPhysicalLeftDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;
        if (!pt.Properties.IsLeftButtonPressed || !isPhysicalLeftDown)
        {
            _isDragCandidate = false;
            return;
        }

        var dx = pt.Position.X - _dragStartPoint.X;
        var dy = pt.Position.Y - _dragStartPoint.Y;
        if ((dx * dx) + (dy * dy) < 16.0) // 4px drag threshold prevents single-click sticky lock
            return;

        _isDragCandidate = false;
        e.Handled = true;
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(_hwnd, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)NativeMethods.HTCAPTION, IntPtr.Zero);
    }

    public void OnDragSurfacePointerReleased() => _isDragCandidate = false;

    /// <summary>
    /// Toggles between maximized and restored states.
    /// Attach this to DoubleTapped on the title bar drag surface.
    /// </summary>
    public void ToggleMaximize()
    {
        _isDragCandidate = false;
        if (_window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            if (presenter.State == OverlappedPresenterState.Maximized)
                presenter.Restore();
            else
                presenter.Maximize();
        }
    }
}
