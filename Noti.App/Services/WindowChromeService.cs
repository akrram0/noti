using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Noti.App.Interop;
using Windows.Graphics;
using WinRT.Interop;

namespace Noti.App.Services;

/// <summary>
/// Manages window chrome configuration (icon, size, title bar, drag behaviour)
/// for a single WinUI 3 Window.
/// </summary>
internal sealed class WindowChromeService
{
    private readonly Window _window;
    private readonly IntPtr _hwnd;

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
        _window.SetTitleBar(null); // let WM_SYSCOMMAND handle dragging — no XAML hit-test loop

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = _window.AppWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar  = true;
            titleBar.PreferredHeightOption       = TitleBarHeightOption.Tall;
            titleBar.ButtonBackgroundColor       = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            // Suppress WinUI 3 drag rectangles — drag is handled by WM_SYSCOMMAND below.
            titleBar.SetDragRectangles([new RectInt32(0, 0, 0, 0)]);
        }
    }

    /// <summary>
    /// Initiates a native OS modal window-move via WM_SYSCOMMAND(SC_MOVE+HTCAPTION).
    /// Attach this to PointerPressed on the title bar drag surface (left button only).
    /// Guarantees 1:1 hardware cursor lock with zero XAML layout overhead.
    /// </summary>
    public void BeginDrag()
    {
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(_hwnd, NativeMethods.WM_SYSCOMMAND,
            (IntPtr)(NativeMethods.SC_MOVE + NativeMethods.HTCAPTION), IntPtr.Zero);
    }

    /// <summary>
    /// Toggles between maximized and restored states.
    /// Attach this to DoubleTapped on the title bar drag surface.
    /// </summary>
    public void ToggleMaximize()
    {
        if (_window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            if (presenter.State == OverlappedPresenterState.Maximized)
                presenter.Restore();
            else
                presenter.Maximize();
        }
    }
}
