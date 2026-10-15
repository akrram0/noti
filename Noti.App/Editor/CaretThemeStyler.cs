using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI.ViewManagement;

namespace Noti.App;

/// <summary>
/// WinUI paints the native <see cref="TextBox"/> caret as a hidden child <see cref="Shape"/> whose Fill is a
/// hardcoded white brush and whose CompositeMode is the internal DestinationInvert value, so the caret renders
/// as the inverse of the app-composited pixels beneath it. Over transparent, Acrylic, Mica, or translucent
/// canvas-tint regions the compositor sees transparent black, which inverts to pure white — no ElementTheme,
/// ActualTheme, Foreground, or TextControlForeground change can ever recolor it.
/// This styler locates that native caret element inside a TextBox's visual tree and flips it to a plain
/// SourceOver shape with a themed Fill. The native blink storyboard, positioning, RTL caret geometry, and
/// show/hide rules all stay on the native caret element; only its color source changes.
/// </summary>
internal static class CaretThemeStyler
{
    // The public ElementCompositeMode enum exposes only Inherit (0), SourceOver (1) and MinBlend (2); the
    // native caret's internal DestinationInvert value (3) is still readable from the projected property.
    private const int DestinationInvertValue = 3;

    private static readonly ConditionalWeakTable<TextBox, CaretRegistration> s_registrations = new();

    private static AccessibilitySettings? s_accessibilitySettings;
    private static bool? s_isHighContrastActive;

    private sealed class CaretRegistration
    {
        public Shape? CaretShape;
    }

    /// <summary>Recolors the native caret of <paramref name="textBox"/> to <paramref name="caretBrush"/>.</summary>
    public static void Apply(TextBox textBox, Brush caretBrush)
    {
        if (textBox is null || caretBrush is null)
            return;

        // High-contrast mode intentionally relies on the native inversion for guaranteed contrast.
        if (IsHighContrastActive())
            return;

        if (!s_registrations.TryGetValue(textBox, out var registration))
        {
            registration = new CaretRegistration();
            s_registrations.Add(textBox, registration);
        }

        if (!IsAlive(registration.CaretShape))
            registration.CaretShape = FindNativeCaretShape(textBox);

        if (registration.CaretShape is not { } caret)
            return;

        caret.Fill = caretBrush;
        caret.CompositeMode = ElementCompositeMode.SourceOver;
    }

    private static bool IsAlive(Shape? caret) =>
        caret is not null &&
        caret.XamlRoot is not null &&
        VisualTreeHelper.GetParent(caret) is not null;

    private static Shape? FindNativeCaretShape(TextBox textBox)
    {
        var pending = new Queue<DependencyObject>();
        pending.Enqueue(textBox);
        while (pending.Count > 0)
        {
            var parent = pending.Dequeue();
            var childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Shape shape && (int)shape.CompositeMode == DestinationInvertValue)
                    return shape;
                pending.Enqueue(child);
            }
        }

        return null;
    }

    private static bool IsHighContrastActive()
    {
        if (s_isHighContrastActive is { } cached)
            return cached;

        try
        {
            if (s_accessibilitySettings is null)
            {
                s_accessibilitySettings = new AccessibilitySettings();
                s_accessibilitySettings.HighContrastChanged += (_, _) => s_isHighContrastActive = null;
            }

            s_isHighContrastActive = s_accessibilitySettings.HighContrast;
        }
        catch
        {
            s_isHighContrastActive = false;
        }

        return s_isHighContrastActive ?? false;
    }
}
