using System.Windows;
using System.Windows.Input;
using Lyricify.Lyrics.Parsers;
using ABLyrics.App.Models;
using ABLyrics.App.Services.Lyrics;

namespace ABLyrics.App.Views;

public partial class CandidateColumnView : System.Windows.Controls.UserControl
{
    private readonly LyricsSyncEngine _engine = new();
    private LyricsCandidate? _candidate;

    /// <summary>当前列被点击选中（用作"最终版本"），由宿主窗口决定后续动作。</summary>
    public static readonly RoutedEvent SelectedEvent = EventManager.RegisterRoutedEvent(
        nameof(Selected), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(CandidateColumnView));

    public event RoutedEventHandler Selected
    {
        add => AddHandler(SelectedEvent, value);
        remove => RemoveHandler(SelectedEvent, value);
    }

    public CandidateColumnView()
    {
        InitializeComponent();
    }

    /// <summary>当前列是否被选作最终版本。仅 UI 高亮，不影响歌词播放。</summary>
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected), typeof(bool), typeof(CandidateColumnView),
        new PropertyMetadata(false, OnIsSelectedChanged));

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CandidateColumnView view) view.UpdateHighlight();
    }

    private void UpdateHighlight()
    {
        if (IsSelected)
        {
            CardBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x21, 0x96, 0xF3));
            CardBorder.BorderThickness = new Thickness(2);
            CardBorder.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x22, 0x21, 0x96, 0xF3));
            SelectedBadge.Visibility = Visibility.Visible;
        }
        else
        {
            CardBorder.BorderBrush = System.Windows.Media.Brushes.Gray;
            CardBorder.BorderThickness = new Thickness(1);
            CardBorder.Background = System.Windows.Media.Brushes.Transparent;
            SelectedBadge.Visibility = Visibility.Collapsed;
        }
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

    private void OnRootClicked(object sender, MouseButtonEventArgs e)
    {
        // 单击列卡 = 选为最终版本
        RaiseEvent(new RoutedEventArgs(SelectedEvent, this));
    }
}
