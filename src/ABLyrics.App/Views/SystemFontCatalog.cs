using System.Windows.Media;

namespace ABLyrics.App.Views;

internal static class SystemFontCatalog
{
    private static readonly Lazy<IReadOnlyList<string>> Cached = new(Load);

    public static IReadOnlyList<string> GetInstalledFontFamilies() => Cached.Value;

    private static IReadOnlyList<string> Load()
    {
        return Fonts.SystemFontFamilies
            .Select(family => family.Source)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
