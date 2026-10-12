using Microsoft.UI;
using Windows.UI;

namespace Noti.App.Styles;

/// <summary>
/// Centralized color swatch palette and adaptive luminance helpers for Noti's Glass/Mica UI.
/// </summary>
internal static class ThemePalette
{
    public static readonly (string Name, string Hex)[] SwatchColors =
    [
        ("Blue", "#FF3B82F6"),
        ("Purple", "#FF8B5CF6"),
        ("Pink", "#FFEC4899"),
        ("Red", "#FFEF4444"),
        ("Amber", "#FFF59E0B"),
        ("Green", "#FF10B981"),
        ("Cyan", "#FF06B6D4")
    ];

    public static Color GetContrastingCheckmarkColor(Color swatchColor)
    {
        var luminance = ((0.299 * swatchColor.R) + (0.587 * swatchColor.G) + (0.114 * swatchColor.B)) / 255.0;
        return luminance > 0.62
            ? ColorHelper.FromArgb(255, 28, 28, 30)
            : Colors.White;
    }
}
