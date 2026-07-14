using Lyricify.Lyrics.Parsers;
using ABLyrics.App.Models;
using ABLyrics.App.Services.Lyrics;

namespace ABLyrics.App.Views;

public partial class CandidateColumnView : System.Windows.Controls.UserControl
{
    private readonly LyricsSyncEngine _engine = new();
    private LyricsCandidate? _candidate;

    public CandidateColumnView()
    {
        InitializeComponent();
    }

    public void Bind(LyricsCandidate candidate)
    {
        _candidate = candidate;
        HeaderText.Text = $"{candidate.Source} · {candidate.Label}";
        _engine.SetDurationMs(candidate.DurationMs);
        if (!string.IsNullOrWhiteSpace(candidate.SyncedLyrics))
        {
            var data = LrcParser.Parse(candidate.SyncedLyrics);
            var plain = (candidate.PlainLyrics ?? string.Empty)
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            _engine.LoadParsed(data, plain);
        }
    }

    public void OnProgress(long progressMs)
    {
        if (_candidate is null) return;
        var frame = _engine.GetFrame(progressMs);
        PreviousLineText.Text = frame.PreviousLine;
        CurrentLineText.Text = frame.CurrentLine;
        NextLineText.Text = frame.NextLine;
    }
}