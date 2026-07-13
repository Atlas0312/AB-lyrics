// This Source Code Form is subject to the terms of the MIT License.
// Copyright (C) SpotLyrics Contributors.

namespace SpotLyrics.App.Views;

/// <summary>
/// Pure logic for mapping a <see cref="Wpf.Ui.Controls.INavigationViewItem.TargetPageTag"/>
/// to the corresponding page region name in <see cref="StyleSettingsWindow"/>.
/// Extracted to keep this decision unit-testable without spinning up a
/// WPF NavigationView, and to avoid relying on
/// <see cref="Wpf.Ui.Controls.NavigationView.SelectedItem"/>, which in WPF-UI 3.1.x
/// is not synchronously updated when a menu item is clicked via
/// <see cref="Wpf.Ui.Controls.NavigationView.ItemInvoked"/> unless the item
/// also has <c>TargetPageType</c> set.
///
/// Contract:
/// - Known, non-empty tag → matching ScrollViewer x:Name.
/// - Unknown / null / empty tag → null (caller should leave state unchanged).
/// </summary>
internal static class StyleSettingsTabRouter
{
    public const string AppearanceTag = "appearance";
    public const string LayoutTag = "layout";
    public const string ColorTag = "color";
    public const string SyncTag = "sync";
    public const string AboutTag = "about";

    public static readonly IReadOnlyCollection<string> KnownTags = new[]
    {
        AppearanceTag,
        LayoutTag,
        ColorTag,
        SyncTag,
        AboutTag,
    };

    /// <summary>
    /// Returns the ScrollViewer x:Name that should be visible for the given
    /// tag, or <c>null</c> if the tag is not a known settings page.
    /// </summary>
    public static string? Resolve(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        return tag switch
        {
            AppearanceTag => "AppearancePage",
            LayoutTag => "LayoutPage",
            ColorTag => "ColorPage",
            SyncTag => "SyncPage",
            AboutTag => "AboutPage",
            _ => null,
        };
    }
}
