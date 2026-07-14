namespace ABLyrics.App.Configuration;

public sealed class DisplayStyleSettings
{
    public string FontFamily { get; set; } = "Microsoft YaHei UI";
    public double FontSize { get; set; } = 18;
    public double SongInfoFontSize { get; set; } = 12;
    public double LetterSpacing { get; set; } = 0;
    public int BarHeight { get; set; } = 56;
    public string BackgroundColor { get; set; } = "#101010";
    public double BackgroundOpacity { get; set; } = 0.8;
    public double PaddingLeft { get; set; } = 16;
    public double PaddingTop { get; set; } = 4;
    public double PaddingRight { get; set; } = 16;
    public double PaddingBottom { get; set; } = 4;
    public int LineCount { get; set; } = 1;
    public int SyncOffsetMs { get; set; } = 150;

    public string ForegroundColor { get; set; } = "#FFFFFF";
    public double ForegroundOpacity { get; set; } = 0.78;
    public string OverlayBaseColor { get; set; } = "#000000";

    public DisplayStyleSettings Clone()
    {
        return new DisplayStyleSettings
        {
            FontFamily = FontFamily,
            FontSize = FontSize,
            SongInfoFontSize = SongInfoFontSize,
            LetterSpacing = LetterSpacing,
            BarHeight = BarHeight,
            BackgroundColor = BackgroundColor,
            BackgroundOpacity = BackgroundOpacity,
            PaddingLeft = PaddingLeft,
            PaddingTop = PaddingTop,
            PaddingRight = PaddingRight,
            PaddingBottom = PaddingBottom,
            LineCount = LineCount,
            SyncOffsetMs = SyncOffsetMs,
            ForegroundColor = ForegroundColor,
            ForegroundOpacity = ForegroundOpacity,
            OverlayBaseColor = OverlayBaseColor,
        };
    }
}
