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
/// Manages window chrome configuration (icon, size, title bar regions, and click-and-hold drag behaviour)
/// for a single WinUI 3 Window.
/// </summary>
internal sealed class WindowChromeService
{
    private readonly Window _window;
    private readonly IntPtr _hwnd;
    private bool _isDragCandidate;
    private Point _dragStartPoint;

    public WindowChromeService(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        _hwnd   = WindowNative.GetWindowHandle(window);
    }

    /// <summary>
    /// Configures the initial window chrome: icon, size, and custom title bar.
    /// Call once from the Window constructor after InitializeComponent().
    /// </summary>
    public void Configure(int width = 1080, int height = 680)
    {
        _window.AppWindow.Resize(new SizeInt32(width, height));

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

    /// <summary>
    /// Updates the native non-client caption drag rectangles and interactive passthrough regions
    /// so DWM handles click-and-hold dragging and top-right Minimize/Maximize/Close buttons natively.
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
            : (int)Math.Ceiling(144 * scale);

        var totalWidthPx  = (int)Math.Round(titleBarContainer.ActualWidth * scale);
        var totalHeightPx = (int)Math.Round(48 * scale);
        var usableWidthPx = Math.Max(0, totalWidthPx - rightCaptionInset);

        try
        {
            var nonClientSource = InputNonClientPointerSource.GetForWindowId(_window.AppWindow.Id);

            // 1. Mark the title bar area (left of native Min/Max/Close buttons) as native DWM Caption
            if (usableWidthPx > 0 && totalHeightPx > 0)
            {
                var captionRect = new RectInt32(0, 0, usableWidthPx, totalHeightPx);
                titleBar.SetDragRectangles([captionRect]);
                nonClientSource.SetRegionRects(NonClientRegionKind.Caption, [captionRect]);
            }

            // 2. Mark interactive XAML controls inside the title bar as Passthrough so clicks hit them directly
            var passthroughRects = new List<RectInt32>();
            foreach (var ctrl in interactiveControls)
            {
                if (ctrl is null || ctrl.Visibility != Visibility.Visible || ctrl.ActualWidth <= 0 || ctrl.ActualHeight <= 0)
                    continue;

                var transform = ctrl.TransformToVisual( null);
                var bounds = transform.TransformBounds(new Rect(0, 0, ctrl.ActualWidth, ctrl.ActualHeight));
                var x = Math.Max(0, (int)Math.Floor(bounds.X * scale));
                var y = Math.Max(0, (int)Math.Floor(bounds.Y * scale));
                var w = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
                var h = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));

                if (x < usableWidthPx)
                {
                    var clampedW = Math.Min(w, usableWidthPx - x);
                    if (clampedW > 0)
                        passthroughRects.Add(new RectInt32(x, y, clampedW, h));
                }
            }

            if (passthroughRects.Count > 0)
                nonClientSource.SetRegionRects(NonClientRegionKind.Passthrough, [.. passthroughRects]);
            else
                nonClientSource.ClearRegionRects(NonClientRegionKind.Passthrough);
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
