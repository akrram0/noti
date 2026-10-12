using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Noti.App.ViewModels;
using WinRT;

namespace Noti.App.Services;

/// <summary>
/// Manages the window system backdrop (Mica or Desktop Acrylic) for a single Window.
/// Owns the DesktopAcrylicController lifetime and all related configuration.
/// Call <see cref="Apply"/> when the backdrop mode or theme changes,
/// <see cref="UpdateOpacity"/> when only the opacity slider moves,
/// and <see cref="Dispose"/> when the window closes.
/// </summary>
internal sealed class BackdropService : IDisposable
{
    private readonly Window _window;
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _backdropConfig;
    private BackdropMode _lastMode = BackdropMode.Blur;
    private bool _lastIsDark = true;
    private double _lastOpacityPercent = 22.0;
    private bool _hasBoundPostActivation;
    private bool _disposed;

    public BackdropService(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        _window.Activated += OnWindowActivated;
    }

    /// <summary>
    /// Applies (or re-applies) the backdrop for the given mode, theme, and opacity.
    /// Safe to call multiple times; disposes and recreates the acrylic controller when switching modes.
    /// </summary>
    public void Apply(BackdropMode mode, bool isDark, double opacityPercent, bool forceRecreate = false)
    {
        if (_disposed)
            return;

        _lastMode = mode;
        _lastIsDark = isDark;
        _lastOpacityPercent = opacityPercent;

        try
        {
            if (mode == BackdropMode.Blur && DesktopAcrylicController.IsSupported())
            {
                if (forceRecreate)
                    DisposeAcrylicController();

                _window.SystemBackdrop = null;

                if (_acrylicController is null)
                {
                    _backdropConfig = new SystemBackdropConfiguration
                    {
                        IsInputActive = true,
                        Theme = isDark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light
                    };
                    var controller = new DesktopAcrylicController();
                    ApplyAcrylicPropertiesTo(controller, _backdropConfig, isDark, opacityPercent);
                    controller.SetSystemBackdropConfiguration(_backdropConfig);
                    controller.AddSystemBackdropTarget(
                        _window.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
                    _acrylicController = controller;
                }
                else
                {
                    ApplyAcrylicProperties(isDark, opacityPercent);
                }

                return;
            }

            DisposeAcrylicController();

            if (MicaController.IsSupported())
                _window.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
            else
                _window.SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        catch
        {
            DisposeAcrylicController();
            try
            {
                _window.SystemBackdrop = mode == BackdropMode.Blur
                    ? new DesktopAcrylicBackdrop()
                    : new MicaBackdrop { Kind = MicaKind.Base };
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Called once from RootGrid.Loaded (after Window.Activate and swapchain creation)
    /// to ensure DWM has a live, post-activation composition target even after rapid close/reopen.
    /// </summary>
    public void EnsurePostActivationBinding(BackdropMode mode, bool isDark, double opacityPercent)
    {
        if (_disposed)
            return;

        var needsRebind = !_hasBoundPostActivation || (mode == BackdropMode.Blur && _acrylicController is null);
        _hasBoundPostActivation = true;
        Apply(mode, isDark, opacityPercent, forceRecreate: needsRebind && mode == BackdropMode.Blur);
    }

    /// <summary>
    /// Updates acrylic tint and luminosity for a live opacity-slider change.
    /// No-op if acrylic is not currently active.
    /// </summary>
    public void UpdateOpacity(bool isDark, double opacityPercent)
    {
        if (_disposed)
            return;

        _lastIsDark = isDark;
        _lastOpacityPercent = opacityPercent;

        if (_acrylicController is null)
            return;

        ApplyAcrylicProperties(isDark, opacityPercent);
    }

    // ── private ────────────────────────────────────────────────────────────

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_disposed)
            return;

        if (_backdropConfig is not null)
            _backdropConfig.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;

        if (args.WindowActivationState != WindowActivationState.Deactivated &&
            _lastMode == BackdropMode.Blur &&
            _acrylicController is null &&
            DesktopAcrylicController.IsSupported())
        {
            Apply(_lastMode, _lastIsDark, _lastOpacityPercent, forceRecreate: true);
        }
    }

    private void ApplyAcrylicProperties(bool isDark, double opacityPercent)
    {
        if (_acrylicController is null)
            return;

        ApplyAcrylicPropertiesTo(_acrylicController, _backdropConfig, isDark, opacityPercent);
    }

    private static void ApplyAcrylicPropertiesTo(
        DesktopAcrylicController controller,
        SystemBackdropConfiguration? config,
        bool isDark,
        double opacityPercent)
    {
        var t = (float)Math.Clamp(opacityPercent / 100.0, 0.0, 1.0);

        if (config is not null)
        {
            config.IsInputActive = true;
            config.Theme = isDark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
        }

        var tintColor = isDark
            ? ColorHelper.FromArgb(255, 10, 10, 14)
            : ColorHelper.FromArgb(255, 248, 248, 252);

        controller.TintColor     = tintColor;
        controller.FallbackColor = tintColor;
        // TintOpacity scales with the user's slider (0 → 50%).
        // LuminosityOpacity has a floor so blur stays visible at 0% tint
        // (without a floor, TintOpacity=0 + LuminosityOpacity=0 → pitch black).
        controller.TintOpacity = Math.Clamp(t * 0.50f, 0.0f, 0.50f);
        var luminosityFloor = isDark ? 0.64f : 0.85f;
        controller.LuminosityOpacity =
            Math.Clamp(luminosityFloor + t * (1.0f - luminosityFloor), luminosityFloor, 1.0f);
    }

    private void DisposeAcrylicController()
    {
        if (_acrylicController is not null)
        {
            try
            {
                _acrylicController.RemoveAllSystemBackdropTargets();
                _acrylicController.Dispose();
            }
            catch
            {
            }
            _acrylicController = null;
        }
        _backdropConfig = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.Activated -= OnWindowActivated;
        DisposeAcrylicController();
    }
}
