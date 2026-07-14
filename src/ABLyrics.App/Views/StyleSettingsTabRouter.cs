// This Source Code Form is subject to the terms of the MIT License.
// Copyright (C) ABLyrics Contributors.

namespace ABLyrics.App.Views;

internal static class StyleSettingsTabRouter
{
    public const string AppearanceTag = "appearance";
    public const string LayoutTag = "layout";
    public const string ColorTag = "color";
    public const string SyncTag = "sync";
    public const string AboutTag = "about";
    public const string PlaybackSourceTag = "playback-source";
    public const string LyricsTag = "lyrics";

    public static readonly IReadOnlyCollection<string> KnownTags = new[]
    {
        AppearanceTag,
        SyncTag,
        AboutTag,
        PlaybackSourceTag,
        LyricsTag,
    };

    public static string? Resolve(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        return tag switch
        {
            AppearanceTag => "AppearancePage",
            SyncTag => "SyncPage",
            AboutTag => "AboutPage",
            PlaybackSourceTag => "PlaybackSourcePage",
            LyricsTag => "LyricsPage",
            _ => null,
        };
    }
}