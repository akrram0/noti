using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Noti.App.ViewModels;
using Windows.Foundation;
using Windows.Graphics;

namespace Noti.App;

public sealed partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;

        InitializeComponent();
        ConfigureWindowChrome();

        RootGrid.Loaded += (_, _) => UpdateTitleBarPassthrough();
        RootGrid.SizeChanged += (_, _) => UpdateTitleBarPassthrough();
    }

    public MainViewModel ViewModel { get; }

    private void ConfigureWindowChrome()
    {
        AppWindow.Resize(new SizeInt32(1040, 640));

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = AppWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = Colors.Gray;
        }
    }

    private void UpdateTitleBarPassthrough()
    {
        if (RootGrid.XamlRoot is null || SidebarToggleButton.ActualWidth <= 0)
            return;

        var scale = RootGrid.XamlRoot.RasterizationScale;
        var transform = SidebarToggleButton.TransformToVisual(null);
        var bounds = transform.TransformBounds(new Rect(0, 0, SidebarToggleButton.ActualWidth, SidebarToggleButton.ActualHeight));

        var rect = new RectInt32(
            (int)Math.Round(bounds.X * scale),
            (int)Math.Round(bounds.Y * scale),
            (int)Math.Round(bounds.Width * scale),
            (int)Math.Round(bounds.Height * scale));

        var nonClientSource = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        nonClientSource.SetRegionRects(NonClientRegionKind.Passthrough, [rect]);
    }
}
