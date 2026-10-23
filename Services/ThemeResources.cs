using System.Windows;
using System.Windows.Media;

namespace KLauncher.Services;

/// <summary>
/// Pushes the active accent palette into the shared K.* design tokens
/// (Themes/KTheme.xaml). Everything that uses them through DynamicResource
/// updates at once, including menus and dialogs that are not part of the
/// main window's visual tree. Brushes are frozen so they cost no change
/// tracking and can be shared freely.
/// </summary>
public static class ThemeResources
{
    public static void Apply(
        Color primary,
        Color secondary,
        Color background,
        Color surface,
        Color card,
        Color control,
        Color outline)
    {
        var resources = Application.Current?.Resources;
        if (resources is null)
            return;

        resources["K.AccentColor"] = primary;
        resources["K.Accent2Color"] = secondary;
        resources["K.Bg"] = Frozen(background);
        resources["K.Surface"] = Frozen(surface);
        resources["K.Card"] = Frozen(card);
        resources["K.Control"] = Frozen(control);
        resources["K.Outline"] = Frozen(outline);
        resources["K.OutlineStrong"] = Frozen(Mix(outline, Colors.White, 0.08));
        resources["K.Accent"] = Frozen(primary);
        resources["K.Accent2"] = Frozen(secondary);
        resources["K.AccentSoft"] = Frozen(WithAlpha(primary, 0x24));
        resources["K.AccentLine"] = Frozen(WithAlpha(primary, 0x66));
        resources["K.OnAccent"] = Frozen(ReadableOn(primary));
    }

    /// <summary>Black or white, whichever reads better on the colour.</summary>
    public static Color ReadableOn(Color color)
    {
        var luminance = RelativeLuminance(color);
        var contrastWithBlack = (luminance + 0.05) / 0.05;
        var contrastWithWhite = 1.05 / (luminance + 0.05);
        return contrastWithBlack >= contrastWithWhite
            ? Color.FromRgb(10, 12, 14)
            : Color.FromRgb(250, 250, 252);
    }

    private static double RelativeLuminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)(from.R + ((to.R - from.R) * amount)),
        (byte)(from.G + ((to.G - from.G) * amount)),
        (byte)(from.B + ((to.B - from.B) * amount)));

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
