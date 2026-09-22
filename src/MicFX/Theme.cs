using System.Windows;
using System.Windows.Media;

namespace MicFX;

/// <summary>
/// Accent color of the UI. The accent brushes are referenced as
/// DynamicResource everywhere (a StaticResource brush used in a style setter
/// is frozen when the style seals, so it could not be recolored), and
/// replacing them in the application resources recolors every open window.
/// The hover and dim shades are derived from the base color the same way the
/// original violet palette was built.
/// </summary>
public static class Theme
{
    public const string DefaultAccent = "#7C5CFF";

    /// <summary>Mid-tone colors that all keep white text readable on buttons.</summary>
    public static readonly (string Name, string Hex)[] Presets =
    {
        ("Violet", DefaultAccent),
        ("Blue", "#3B7CF6"),
        ("Cyan", "#0891B2"),
        ("Teal", "#0F9D8A"),
        ("Green", "#22A04B"),
        ("Amber", "#C8741F"),
        ("Rose", "#E0457B"),
        ("Slate", "#6B7385"),
    };

    public static Color Parse(string? hex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex) is Color c)
                return Color.FromRgb(c.R, c.G, c.B);
        }
        catch (FormatException)
        {
        }
        return (Color)ColorConverter.ConvertFromString(DefaultAccent);
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static void ApplyAccent(Color accent)
    {
        var resources = Application.Current.Resources;
        var window = ((SolidColorBrush)resources["WindowBrush"]).Color;

        // Dim shade: 37% of the accent over the window background, as in the
        // original palette (#3A2F6E from #7C5CFF), pulled darker for a very
        // light custom color so text on it stays readable.
        double t = 0.37;
        Color dim = Mix(window, accent, t);
        while (Luminance(dim) > 0.06 && t > 0.1)
            dim = Mix(window, accent, t -= 0.03);

        resources["AccentBrush"] = Frozen(accent);
        resources["AccentHoverBrush"] = Frozen(Mix(accent, Colors.White, 0.15));
        resources["AccentDimBrush"] = Frozen(dim);
        // Text and check marks drawn on the accent: white unless the accent is
        // so light that white would disappear on it.
        resources["OnAccentBrush"] = Frozen(Luminance(accent) > 0.4 ? window : Colors.White);
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    private static Color Mix(Color from, Color to, double t) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * t),
        (byte)Math.Round(from.G + (to.G - from.G) * t),
        (byte)Math.Round(from.B + (to.B - from.B) * t));

    /// <summary>WCAG relative luminance, 0 (black) to 1 (white).</summary>
    private static double Luminance(Color c)
    {
        static double Linear(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }
}
