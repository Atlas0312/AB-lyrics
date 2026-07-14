using System.Globalization;

namespace ABLyrics.App.Services;

/// <summary>
/// Picks between pure white and pure black as a recommended foreground color
/// using WCAG 2.x contrast (target 4.5:1 = AA for normal text) on the
/// effective background after alpha-blending the configured overlay base.
/// </summary>
internal static class ColorContrastHelper
{
    public const string White = "#FFFFFF";
    public const string Black = "#000000";
    private const double TargetContrast = 4.5;

    public static string RecommendForeground(
        string backgroundHex,
        double backgroundOpacity,
        string overlayBaseHex)
    {
        if (!TryParseColor(backgroundHex, out var bg))
        {
            return Black;
        }

        var baseColor = TryParseColor(overlayBaseHex, out var parsedBase) ? parsedBase : ((byte)0, (byte)0, (byte)0);
        var alpha = ClampOpacity(backgroundOpacity);

        var effective = BlendOver(bg, baseColor, alpha);
        var lBg = RelativeLuminance(effective);

        var whiteContrast = ContrastRatio(lBg, 1.0);
        var blackContrast = ContrastRatio(lBg, 0.0);

        var whiteOk = whiteContrast >= TargetContrast;
        var blackOk = blackContrast >= TargetContrast;

        if (whiteOk && !blackOk) return White;
        if (blackOk && !whiteOk) return Black;
        if (whiteOk && blackOk) return whiteContrast >= blackContrast ? White : Black;
        return whiteContrast >= blackContrast ? White : Black;
    }

    private static double ClampOpacity(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return 1.0;
        if (value < 0) return 0;
        if (value > 1) return 1;
        return value;
    }

    private static (byte R, byte G, byte B) BlendOver(
        (byte R, byte G, byte B) bg,
        (byte R, byte G, byte B) baseColor,
        double alpha)
    {
        return (
            (byte)Math.Round(bg.R * alpha + baseColor.R * (1 - alpha)),
            (byte)Math.Round(bg.G * alpha + baseColor.G * (1 - alpha)),
            (byte)Math.Round(bg.B * alpha + baseColor.B * (1 - alpha)));
    }

    private static double RelativeLuminance((byte R, byte G, byte B) color)
    {
        return 0.2126 * Linearize(color.R)
             + 0.7152 * Linearize(color.G)
             + 0.0722 * Linearize(color.B);
    }

    private static double Linearize(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double ContrastRatio(double l1, double l2)
    {
        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static bool TryParseColor(string hex, out (byte R, byte G, byte B) color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var value = hex.Trim().TrimStart('#');
        if (value.Length != 6) return false;
        if (!byte.TryParse(value.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)) return false;
        if (!byte.TryParse(value.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)) return false;
        if (!byte.TryParse(value.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)) return false;
        color = (r, g, b);
        return true;
    }
}
