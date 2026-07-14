using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ABLyrics.App.Configuration;
using ABLyrics.App.Models;
using ABLyrics.App.Services;
using ABLyrics.App.Services.Lyrics;

namespace ABLyrics.App.Views;

public partial class LyricsCandidatePickerWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly PlaybackCoordinator _coordinator;
    private readonly ILyricsSearchService _searchService;
    private readonly LyricsOverrideStore _overrideStore;
    private readonly ObservableCollection<CandidateRow> _candidates = new();
    private TrackInfo? _currentTrack;
    private CandidateRow? _selectedRow;
    private bool _suppressSelectionChanged;

    public LyricsCandidatePickerWindow(
        PlaybackCoordinator coordinator,
        ILyricsSearchService searchService,
        LyricsOverrideStore overrideStore,
        TrackInfo? track = null)
    {
        InitializeComponent();
        _coordinator = coordinator;
        _searchService = searchService;
        _overrideStore = overrideStore;
        _currentTrack = track;

        CandidatesList.ItemsSource = _candidates;
        Closing += OnClosingWindow;

        _coordinator.ProgressMsChanged += OnProgressMsChanged;

        Loaded += OnLoaded;
    }

    /// <summary>
    /// 候选项视图模型：包装 <see cref="LyricsCandidate"/> + 选中状态 + 当前覆盖项标记。
    /// </summary>
    private sealed class CandidateRow : INotifyPropertyChanged
    {
        private bool _isSelected;
        public LyricsCandidate Candidate { get; init; } = null!;
        public bool IsOverride { get; set; }
        public bool IsAvailable { get; init; } = true;

        public string DisplayText => $"{Candidate.Source} · {Candidate.Label}";
        public string OriginDescription => Candidate.Origin switch
        {
            CandidateOrigin.Local l => $"本地文件：{l.FilePath}",
            CandidateOrigin.Lrclib l => $"LRCLIB id：{l.LrclibId}",
            CandidateOrigin.Netease n => $"Netease id：{n.NeteaseSongId}",
            _ => string.Empty,
        };

        public Visibility IsOverrideVisibility => IsOverride ? Visibility.Visible : Visibility.Collapsed;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_currentTrack is null)
        {
            StatusText.Text = "暂无曲目：请先开始播放";
            LibraryCombo.IsEnabled = false;
            return;
        }

        TrackInfoText.Text = $"{_currentTrack.Artist} — {_currentTrack.Name} ({_currentTrack.Album})";
        LibraryCombo.ItemsSource = _coordinator.AvailableSources;
        if (LibraryCombo.Items.Count > 0)
        {
            LibraryCombo.SelectedIndex = 0;
        }
        ConfirmButton.IsEnabled = false;
        await SearchAsync(_currentTrack, LibraryCombo.SelectedValue as string);
    }

    private async Task SearchAsync(TrackInfo track, string? library)
    {
        if (string.IsNullOrEmpty(library)) return;

        StatusText.Text = $"正在搜索 {library}…";
        IReadOnlyList<LyricsCandidate> results;
        try
        {
            results = await _searchService.SearchAsync(track, library).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            DevExceptionReporter.Show(ex, $"搜索 {library} 失败");
            StatusText.Text = $"搜索 {library} 失败";
            return;
        }

        var overrides = _overrideStore.Load();
        var key = TrackKey.From(track);

        _suppressSelectionChanged = true;
        _candidates.Clear();
        foreach (var c in results)
        {
            _candidates.Add(new CandidateRow
            {
                Candidate = c,
                IsAvailable = c.IsAvailable,
                IsOverride = overrides.TryGetValue(key, out var origin)
                             && OriginEquals(origin, c.Origin),
            });
        }
        _suppressSelectionChanged = false;

        StatusText.Text = _candidates.Count == 0
            ? $"{library}：无候选"
            : $"{library}：{_candidates.Count} 个候选";
    }

    private static bool OriginEquals(CandidateOrigin a, CandidateOrigin b) => a switch
    {
        CandidateOrigin.Local l1 when b is CandidateOrigin.Local l2 =>
            string.Equals(l1.FilePath, l2.FilePath, StringComparison.OrdinalIgnoreCase),
        CandidateOrigin.Lrclib x1 when b is CandidateOrigin.Lrclib x2 =>
            x1.LrclibId == x2.LrclibId,
        CandidateOrigin.Netease n1 when b is CandidateOrigin.Netease n2 =>
            n1.NeteaseSongId == n2.NeteaseSongId,
        _ => false,
    };

    private void OnLibrarySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_currentTrack is null) return;
        _ = SearchAsync(_currentTrack, LibraryCombo.SelectedValue as string);
    }

    private void OnCandidateSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged) return;
        _selectedRow = CandidatesList.SelectedItem as CandidateRow;
        UpdateCompareArea();
    }

    private void UpdateCompareArea()
    {
        if (_selectedRow is null)
        {
            CompareColumns.Visibility = Visibility.Collapsed;
            EmptyCompareText.Visibility = Visibility.Visible;
            ConfirmButton.IsEnabled = false;
            return;
        }

        var single = new[] { _selectedRow.Candidate };
        CompareColumns.ItemsSource = single;
        CompareColumns.Visibility = Visibility.Visible;
        EmptyCompareText.Visibility = Visibility.Collapsed;
        ConfirmButton.IsEnabled = true;

        // 触发首帧（用当前进度 ms）
        var progressMs = _coordinator.GetCurrentTrackId() == _currentTrack?.Id
            ? Math.Max(0, _coordinator.CurrentLine.Length > 0 ? 1L : 0L)
            : 0L;
        OnProgressMsChanged(progressMs);
    }

    private void OnProgressMsChanged(long progressMs)
    {
        if (CompareColumns.Visibility != Visibility.Visible) return;

        // ItemsControl 的 ItemsPanel 是 UniformGrid，每列的 Content 是 CandidateColumnView。
        // 通过 ContainerFromIndex 拿到 presenter，再 FindVisualChild 拿到列视图。
        Dispatcher.BeginInvoke(() =>
        {
            for (var i = 0; i < CompareColumns.Items.Count; i++)
            {
                if (CompareColumns.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement container)
                {
                    var column = FindVisualChild<CandidateColumnView>(container);
                    column?.OnProgress(progressMs);
                }
            }
        });
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var deeper = FindVisualChild<T>(child);
            if (deeper is not null) return deeper;
        }
        return null;
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (_currentTrack is null) return;
        await SearchAsync(_currentTrack, LibraryCombo.SelectedValue as string);
    }

    private async void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (_selectedRow is null || _currentTrack is null) return;
        await _coordinator.ApplyCandidateAsync(_selectedRow.Candidate);
        Hide();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void OnClosingWindow(object? sender, CancelEventArgs e)
    {
        // 单例：拦截关闭，只 Hide。
        e.Cancel = true;
        Hide();
    }
}