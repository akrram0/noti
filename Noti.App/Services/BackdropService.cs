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
    private bool _disposed;

    public BackdropService(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
    }

    /// <summary>
    /// Applies (or re-applies) the backdrop for the given mode, theme, and opacity.
    /// Safe to call multiple times; disposes and recreates the acrylic controller when switching modes.
    /// </summary>
    public void Apply(BackdropMode mode, bool isDark, double opacityPercent)
    {
        try
        {
            if (mode == BackdropMode.Blur && DesktopAcrylicController.IsSupported())
            {
                _window.SystemBackdrop = null;

                if (_acrylicController is null)
                {
                    _backdropConfig = new SystemBackdropConfiguration
                    {
                        IsInputActive = true,
                        Theme = isDark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light
                    };
                    _acrylicController = new DesktopAcrylicController();
                    _acrylicController.AddSystemBackdropTarget(
                        _window.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
                    _acrylicController.SetSystemBackdropConfiguration(_backdropConfig);
                }

                ApplyAcrylicProperties(isDark, opacityPercent);
                return;
            }

            DisposeAcrylicController();

            if (MicaController.IsSupported())
                _window.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
        }
        catch
        {
            // Backdrop is non-critical — swallow silently so the app still opens.
        }
    }

    /// <summary>
    /// Updates acrylic tint and luminosity for a live opacity-slider change.
    /// No-op if acrylic is not currently active.
    /// </summary>
    public void UpdateOpacity(bool isDark, double opacityPercent)
    {
        if (_acrylicController is null)
            return;

        ApplyAcrylicProperties(isDark, opacityPercent);
    }

    // ── private ────────────────────────────────────────────────────────────

    private void ApplyAcrylicProperties(bool isDark, double opacityPercent)
    {
        if (_acrylicController is null)
            return;

        var t = (float)Math.Clamp(opacityPercent / 100.0, 0.0, 1.0);

        if (_backdropConfig is not null)
        {
            _backdropConfig.IsInputActive = true;
            _backdropConfig.Theme = isDark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
        }

        var tintColor = isDark
            ? ColorHelper.FromArgb(255, 10, 10, 14)
            : ColorHelper.FromArgb(255, 248, 248, 252);

        _acrylicController.TintColor    = tintColor;
        _acrylicController.FallbackColor = tintColor;
        // TintOpacity scales with the user's slider (0 → 50%).
        // LuminosityOpacity has a floor so blur stays visible at 0% tint
        // (without a floor, TintOpacity=0 + LuminosityOpacity=0 → pitch black).
        _acrylicController.TintOpacity = Math.Clamp(t * 0.50f, 0.0f, 0.50f);
        var luminosityFloor = isDark ? 0.64f : 0.85f;
        _acrylicController.LuminosityOpacity =
            Math.Clamp(luminosityFloor + t * (1.0f - luminosityFloor), luminosityFloor, 1.0f);
    }

    private void DisposeAcrylicController()
    {
        if (_acrylicController is not null)
        {
            _acrylicController.RemoveAllSystemBackdropTargets();
            _acrylicController.Dispose();
            _acrylicController = null;
        }
        _backdropConfig = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeAcrylicController();
    }
}
