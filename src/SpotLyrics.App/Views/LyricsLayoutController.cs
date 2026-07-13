using System.Windows;
using System.Windows.Controls;

namespace SpotLyrics.App.Views;

internal static class LyricsLayoutController
{
    public static void ApplyAppBarLayout(
        Panel trackInfoPanel,
        UIElement lyricsPanel,
        bool shouldCenterTrackInfo)
    {
        if (shouldCenterTrackInfo)
        {
            Grid.SetColumn(trackInfoPanel, 1);
            Grid.SetColumnSpan(trackInfoPanel, 1);
            trackInfoPanel.HorizontalAlignment = HorizontalAlignment.Center;
            lyricsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        Grid.SetColumn(trackInfoPanel, 0);
        Grid.SetColumnSpan(trackInfoPanel, 1);
        trackInfoPanel.HorizontalAlignment = HorizontalAlignment.Left;
        lyricsPanel.Visibility = Visibility.Visible;
    }

    public static void ApplyOverlayLayout(
        Panel trackInfoPanel,
        UIElement lyricsPanel,
        bool shouldCenterTrackInfo)
    {
        trackInfoPanel.HorizontalAlignment = shouldCenterTrackInfo
            ? HorizontalAlignment.Center
            : HorizontalAlignment.Left;
        lyricsPanel.Visibility = shouldCenterTrackInfo ? Visibility.Collapsed : Visibility.Visible;
    }
}
