using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ABLyrics.App.Configuration;
using ABLyrics.App.Models;
using ABLyrics.App.Services;
using ABLyrics.App.Services.Lyrics;

namespace ABLyrics.App.Views;

public partial class LyricsCandidatePickerWindow : Wpf.Ui.Controls.FluentWindow
{
    /// <summary>对比区最多并排显示的列数。</summary>
    private const int MaxCompareColumns = 3;

    private readonly PlaybackCoordinator _coordinator;
    private readonly ILyricsSearchService _searchService;
    private readonly LyricsOverrideStore _overrideStore;
    private readonly ObservableCollection<CandidateRow> _candidates = new();
    private TrackInfo? _currentTrack;
    private string? _currentOverrideKey;
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
    /// 由 <see cref="App.ShowCandidatePicker"/> 在每次开窗前调用：把当前在播的曲目
    /// 同步到窗口，避免单例复用导致一直显示首次打开时的歌曲。
    /// 若 track 变化，会清空候选列表并按当前 LibraryCombo 选项重新搜索。
    /// </summary>
    public void RebindForTrack(TrackInfo? track)
    {
        // 已加载同名同 Artist+Album 曲目时不做无意义重搜。
        if (track is not null
            && _currentTrack is not null
            && string.Equals(track.Id, _currentTrack.Id, StringComparison.Ordinal)
            && string.Equals(track.Artist, _currentTrack.Artist, StringComparison.Ordinal)
            && string.Equals(track.Name, _currentTrack.Name, StringComparison.Ordinal)
            && string.Equals(track.Album, _currentTrack.Album, StringComparison.Ordinal))
        {
            if (!IsVisible)
            {
                Show();
                Activate();
            }
            return;
        }

        _currentTrack = track;
        _currentOverrideKey = null;
        _candidates.Clear();
        CompareColumns.ItemsSource = null;
        CompareColumns.Visibility = Visibility.Collapsed;
        EmptyCompareText.Visibility = Visibility.Visible;
        ConfirmButton.IsEnabled = false;
        UpdateSelectionSummary();

        if (track is null)
        {
            TrackInfoText.Text = "暂无曲目：请先开始播放";
            StatusText.Text = string.Empty;
            LibraryCombo.IsEnabled = false;
            return;
        }

        TrackInfoText.Text = $"{track.Artist} — {track.Name} ({track.Album})";
        LibraryCombo.IsEnabled = true;
        var library = LibraryCombo.SelectedValue as string;
        if (!string.IsNullOrEmpty(library))
        {
            _ = SearchAsync(track, library);
        }
    }

    /// <summary>
    /// 候选项视图模型：包装 <see cref="LyricsCandidate"/> + 选中状态 + 当前覆盖项标记。
    /// </summary>
    private sealed class CandidateRow : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isOverride;
        public LyricsCandidate Candidate { get; init; } = null!;
        public bool IsAvailable { get; init; } = true;

        public bool IsOverride
        {
            get => _isOverride;
            set
            {
                if (_isOverride == value) return;
                _isOverride = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOverride)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOverrideVisibility)));
            }
        }

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
        // 首次 Loaded 时初始化 LibraryCombo；之后由 RebindForTrack 接管切歌重绑。
        if (LibraryCombo.ItemsSource is null)
        {
            LibraryCombo.ItemsSource = _coordinator.AvailableSources;
            if (LibraryCombo.Items.Count > 0)
            {
                LibraryCombo.SelectedIndex = 0;
            }
        }

        if (_currentTrack is null)
        {
            StatusText.Text = "暂无曲目：请先开始播放";
            LibraryCombo.IsEnabled = false;
            UpdateSelectionSummary();
            return;
        }

        TrackInfoText.Text = $"{_currentTrack.Artist} — {_currentTrack.Name} ({_currentTrack.Album})";
        ConfirmButton.IsEnabled = false;
        // _candidates 非空表示 App 层已经通过 RebindForTrack 触发过搜索，
        // 避免重复请求。
        if (_candidates.Count == 0)
        {
            await SearchAsync(_currentTrack, LibraryCombo.SelectedValue as string);
        }
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
        _currentOverrideKey = overrides.ContainsKey(key) ? key : null;

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
        UpdateSelectionSummary();
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

    private void OnCandidateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged) return;
        // 同步 IsSelected 标记（CheckBox 用 OneWay 绑定，纯展示）
        foreach (var row in _candidates)
        {
            var want = CandidatesList.SelectedItems.Contains(row);
            if (row.IsSelected != want)
            {
                row.IsSelected = want;
            }
        }
        UpdateCompareArea();
    }

    /// <summary>
    /// 候选行上的「✕」覆盖项清除按钮。移除 override 并刷新本行 IsOverride 标记。
    /// </summary>
    private void OnClearOverrideClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not CandidateRow row) return;
        if (_currentOverrideKey is null) return;
        _overrideStore.Remove(_currentOverrideKey);
        row.IsOverride = false;
        _currentOverrideKey = null;
        UpdateSelectionSummary();
    }

    private IList<CandidateRow> GetSelectedRows()
    {
        return CandidatesList.SelectedItems.OfType<CandidateRow>().ToList();
    }

    private void UpdateSelectionSummary()
    {
        var selected = GetSelectedRows().Count;
        var total = _candidates.Count;
        SelectionSummaryText.Text = total == 0
            ? string.Empty
            : $"已选 {selected}/{Math.Min(total, MaxCompareColumns)}";
    }

    private void UpdateCompareArea()
    {
        var selected = GetSelectedRows();
        UpdateSelectionSummary();

        if (selected.Count == 0)
        {
            CompareColumns.Visibility = Visibility.Collapsed;
            EmptyCompareText.Visibility = Visibility.Visible;
            ConfirmButton.IsEnabled = false;
            StatusText.Text = _candidates.Count == 0 ? StatusText.Text : "已取消所有勾选";
            return;
        }

        // 限制最多 MaxCompareColumns 列；多余提示用户。
        var display = selected.Take(MaxCompareColumns).Select(r => r.Candidate).ToList();
        CompareColumns.ItemsSource = display;
        CompareColumns.Visibility = Visibility.Visible;
        EmptyCompareText.Visibility = Visibility.Collapsed;

        // 重新生成列后高亮态被重置，所以这里总是禁用「应用所选版本」，
        // 由右侧列的 Selected 事件来启用（详见 OnCompareColumnSelected）。
        ConfirmButton.IsEnabled = false;

        var extra = selected.Count > MaxCompareColumns ? selected.Count - MaxCompareColumns : 0;
        StatusText.Text = extra > 0
            ? $"对比中：已显示前 {MaxCompareColumns} 个（共 {selected.Count}），点击其中一列选作最终版本"
            : $"对比中：已选 {selected.Count} 个，点击其中一列选作最终版本";

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

    /// <summary>
    /// 每列 <see cref="CandidateColumnView"/> 在实例化后立即回调，把候选数据灌进去。
    /// </summary>
    private void OnCompareColumnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not CandidateColumnView column) return;
        if (column.DataContext is not LyricsCandidate candidate) return;
        column.Bind(candidate);
        // 立刻用当前进度 ms 触发一次首帧，避免等下次 ProgressMsChanged 才出字
        var progressMs = _coordinator.GetCurrentTrackId() == _currentTrack?.Id
            ? Math.Max(0, _coordinator.CurrentLine.Length > 0 ? 1L : 0L)
            : 0L;
        column.OnProgress(progressMs);
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
        if (_currentTrack is null) return;
        var final = FindSelectedCompareColumn()?.DataContext as LyricsCandidate;
        if (final is null) return;
        try
        {
            await _coordinator.ApplyCandidateAsync(final);
        }
        catch (Exception ex)
        {
            DevExceptionReporter.Show(ex, "应用候选失败");
            return;
        }
        Hide();
    }

    /// <summary>
    /// 右侧对比区某列被点击 → 选作"最终版本"。
    /// 同步：把其他列的 IsSelected 清掉；启用底部「应用所选版本」按钮。
    /// </summary>
    private void OnCompareColumnSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not CandidateColumnView clicked) return;
        if (CompareColumns.Visibility != Visibility.Visible) return;

        foreach (var column in EnumerateCompareColumns())
        {
            if (!ReferenceEquals(column, clicked))
            {
                column.IsSelected = false;
            }
        }
        clicked.IsSelected = true;
        ConfirmButton.IsEnabled = true;
        StatusText.Text = "已选最终版本，点 [应用所选版本] 写入主窗口";
    }

    private IEnumerable<CandidateColumnView> EnumerateCompareColumns()
    {
        for (var i = 0; i < CompareColumns.Items.Count; i++)
        {
            if (CompareColumns.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement container)
            {
                var column = FindVisualChild<CandidateColumnView>(container);
                if (column is not null) yield return column;
            }
        }
    }

    private CandidateColumnView? FindSelectedCompareColumn()
    {
        foreach (var column in EnumerateCompareColumns())
        {
            if (column.IsSelected) return column;
        }
        return null;
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
