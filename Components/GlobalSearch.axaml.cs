using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Mdhub.Services;
using Mdhub.Text;
using Mdhub.Localization;

namespace Mdhub.Components;

public sealed record WorkspaceReplaceRequest(IReadOnlyList<SearchFile> Files, string Query, string Replacement, FindOptions Options);

public partial class GlobalSearch : UserControl
{
    private static readonly Geometry CollapsedIcon = Geometry.Parse("M 6,4 L 10,8 6,12");
    private static readonly Geometry ExpandedIcon = Geometry.Parse("M 4,6 L 8,10 12,6");
    private string? _root;
    private CancellationTokenSource? _search;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _replacing;
    private string _resultQuery = "";
    private string? _resultRoot; // item 61: the folder the shown results belong to (null: none shown)
    private FindOptions _resultOptions;
    public WorkspaceSearchResult Result { get; private set; } = new([]);
    internal int SearchRuns { get; private set; } // searches actually started (tests)
    public Func<IReadOnlyDictionary<string, string>>? OpenBuffers { get; set; }
    public event Action<SearchFile, SearchHit>? HitRequested;
    public Func<WorkspaceReplaceRequest, Task<string>>? ReplaceRequested { get; set; }
    private FindOptions Options => new(GlobalMatchCase.IsChecked == true, GlobalWholeWord.IsChecked == true, GlobalRegex.IsChecked == true, GlobalPreserveCase.IsChecked == true);
    public GlobalSearch()
    {
        InitializeComponent();
        GlobalQuery.TextChanged += (_, _) => Schedule();
        foreach (var toggle in new[] { GlobalMatchCase, GlobalWholeWord, GlobalRegex }) toggle.IsCheckedChanged += (_, _) => Schedule();
        GlobalExpandReplace.IsCheckedChanged += (_, _) =>
        {
            var expanded = GlobalExpandReplace.IsChecked == true;
            GlobalReplaceRow.IsVisible = expanded;
            GlobalExpandIcon.Data = expanded ? ExpandedIcon : CollapsedIcon;
        };
        GlobalRefresh.Click += async (_, _) => await SearchAsync();
        GlobalCollapseAll.Click += (_, _) => { foreach (var group in GlobalResults.Items.OfType<TreeViewItem>()) group.IsExpanded = false; };
        GlobalQuery.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await SearchAsync(); } };
        _debounce.Tick += async (_, _) => await SearchAsync();
        GlobalResults.SelectionChanged += (_, _) =>
        {
            UpdateButtons();
            if (GlobalResults.SelectedItem is TreeViewItem { Tag: SearchHit hit } && Result.Files.FirstOrDefault(f => f.Path == hit.Path) is { } file)
                HitRequested?.Invoke(file, hit);
        };
        GlobalReplaceSelected.Click += async (_, _) => await ReplaceAsync(false);
        GlobalReplaceAll.Click += async (_, _) => await ReplaceAsync(true);
        DetachedFromVisualTree += (_, _) => { _search?.Cancel(); _debounce.Stop(); };
        AttachedToVisualTree += (_, _) => { Loc.Changed += UpdateSummary; UpdateSummary(); };
        DetachedFromVisualTree += (_, _) => Loc.Changed -= UpdateSummary;
    }
    // Item 61: results shown (or being searched) still match the folder, query and options, so returning to the
    // search view keeps them, with their expanded groups and scroll position, instead of searching again.
    internal bool IsCurrent => _root is not null && (_debounce.IsEnabled || _search is not null
        || (_resultRoot == _root && _resultQuery == (GlobalQuery.Text ?? "") && _resultOptions == Options));
    public void SetFolder(string? root)
    {
        if (_root == root) return;
        _root = root;
        Schedule();
    }
    public void Open(bool replace)
    {
        if (replace) GlobalExpandReplace.IsChecked = true;
        GlobalQuery.Focus();
        Dispatcher.UIThread.Post(() => { GlobalQuery.Focus(); GlobalQuery.SelectAll(); }, DispatcherPriority.Loaded);
        if (!IsCurrent) Schedule();
    }
    // The summary is kept as a function so a language switch re-renders it without searching again.
    private Func<string> _summary = () => "";
    private void SetSummary(Func<string> summary) { _summary = summary; UpdateSummary(); }
    private void UpdateSummary() => GlobalSummary.Text = _summary();
    public void Schedule()
    {
        _search?.Cancel();
        _debounce.Stop();
        Result = new([]);
        _resultRoot = null;
        GlobalResults.Items.Clear();
        UpdateButtons();
        if (_root is null) { SetSummary(() => Loc.T("GlobalOpenFolder")); return; }
        if (string.IsNullOrEmpty(GlobalQuery.Text)) { SetSummary(() => Loc.T("GlobalHint")); return; }
        SetSummary(() => Loc.T("Searching"));
        _debounce.Start();
    }
    public async Task SearchAsync()
    {
        _debounce.Stop();
        _search?.Cancel();
        if (_replacing || _root is null || string.IsNullOrEmpty(GlobalQuery.Text)) { if (!_replacing) Schedule(); return; }
        using var cancellation = new CancellationTokenSource();
        _search = cancellation;
        SearchRuns++;
        var query = GlobalQuery.Text;
        var options = Options;
        SetSummary(() => Loc.T("Searching"));
        Result = new([]);
        GlobalResults.Items.Clear();
        UpdateButtons();
        try
        {
            // Item 46: files stream in from worker threads; batch them onto the UI thread and insert each group in path order.
            var arrived = new System.Collections.Concurrent.ConcurrentQueue<SearchFile>();
            var streamed = new List<SearchFile>();
            var flushPosted = 0;
            var root = _root;
            void Flush()
            {
                Interlocked.Exchange(ref flushPosted, 0);
                if (cancellation.IsCancellationRequested || _search != cancellation) return;
                var added = false;
                while (arrived.TryDequeue(out var file))
                {
                    var index = streamed.BinarySearch(file, Comparer<SearchFile>.Create((x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.Path, y.Path)));
                    if (index < 0) index = ~index;
                    streamed.Insert(index, file);
                    GlobalResults.Items.Insert(index, BuildGroup(root, file));
                    added = true;
                }
                if (!added) return;
                Result = new(streamed.ToArray());
                var count = Result.Count;
                var fileCount = streamed.Count;
                SetSummary(() => Loc.T("Searching") + "\n" + Loc.F("GlobalResults", count, fileCount));
            }
            var result = await WorkspaceSearch.SearchAsync(root, query, options, OpenBuffers?.Invoke() ?? new Dictionary<string, string>(), cancellation.Token, file =>
            {
                arrived.Enqueue(file);
                if (Interlocked.Exchange(ref flushPosted, 1) == 0) Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
            });
            if (cancellation.IsCancellationRequested || result.Cancelled) return;
            Flush();
            _resultQuery = query;
            _resultOptions = options;
            _resultRoot = root;
            if (result.Error is not null || streamed.Count != result.Files.Count)
            {
                GlobalResults.Items.Clear();
                foreach (var file in result.Files) GlobalResults.Items.Add(BuildGroup(root, file));
            }
            Result = result;
            SetSummary(() => result.Error ?? Loc.F("GlobalResults", result.Count, result.Files.Count) +
                (result.Limited ? Loc.F("GlobalLimited", WorkspaceSearch.MaxResults) : "") +
                (result.Skipped > 0 ? Loc.F("GlobalSkipped", result.Skipped) : "") +
                (result.Lossy > 0 ? Loc.F("GlobalLossy", result.Lossy) : ""));
            GlobalQuery.Classes.Set("invalid", result.Error is not null);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!cancellation.IsCancellationRequested) SetSummary(() => ex.Message); }
        finally { if (_search == cancellation) _search = null; UpdateButtons(); }
    }
    private static TreeViewItem BuildGroup(string root, SearchFile file)
    {
        var relative = System.IO.Path.GetRelativePath(root, file.Path);
        var groupHeader = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = FileRowHeader.Gap };
        // Item 78: the file-type icon, then the path and hit count; match rows below start at the path text.
        groupHeader.Children.Add(new FileTypeIcon(file.Path));
        groupHeader.Children.Add(new TextBlock { Text = $"{relative}  ({file.Hits.Count})", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        var group = new TreeViewItem { Header = groupHeader, Tag = file, IsExpanded = true };
        ToolTip.SetTip(group, relative);
        foreach (var hit in file.Hits)
        {
            var label = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Avalonia.Thickness(Mdhub.Services.FileIcon.Size + FileRowHeader.Gap, 0, 0, 0) };
            label.Inlines!.Add(new Run($"{hit.Line}: ") { Foreground = Mdhub.Services.ThemeService.Brush("SearchLineNumberForeground") });
            var offset = Math.Clamp(hit.Start - hit.PreviewStart, 0, hit.Preview.Length);
            var length = Math.Min(hit.Length, hit.Preview.Length - offset);
            label.Inlines.Add(new Run(hit.Preview[..offset]));
            label.Inlines.Add(new Run(hit.Preview.Substring(offset, length)) { Foreground = Mdhub.Services.ThemeService.Brush("SearchMatchForeground"), Background = Mdhub.Services.ThemeService.Brush("SearchMatchBackground") });
            label.Inlines.Add(new Run(hit.Preview[(offset + length)..]));
            var row = new TreeViewItem { Header = label, Tag = hit };
            // Item 43: match rows sit directly under the file name. The template indents each level by
            // the tree indent; give the row its file group's indent instead (a local value beats the template's).
            row.TemplateApplied += (_, e) =>
            {
                if (e.NameScope.Find<Control>("PART_Header") is { } header) header.Margin = new Avalonia.Thickness(0);
            };
            ToolTip.SetTip(row, hit.Preview);
            group.Items.Add(row);
        }
        return group;
    }
    private void UpdateButtons()
    {
        GlobalReplaceAll.IsEnabled = !_replacing && _search is null && Result.Count > 0 && !Result.Limited && Result.Error is null;
        GlobalReplaceSelected.IsEnabled = !_replacing && _search is null && Result.Count > 0 && GlobalResults.SelectedItem is TreeViewItem { Tag: SearchFile or SearchHit };
    }
    private async Task ReplaceAsync(bool all)
    {
        if (_replacing || ReplaceRequested is null || Result.Count == 0) return;
        IReadOnlyList<SearchFile> files = Result.Files;
        if (!all)
        {
            if (GlobalResults.SelectedItem is TreeViewItem { Tag: SearchFile file }) files = [file];
            else if (GlobalResults.SelectedItem is TreeViewItem { Tag: SearchHit hit })
                files = [Result.Files.Single(f => f.Path == hit.Path) with { Hits = [hit] }];
            else return;
        }
        _replacing = true;
        _search?.Cancel();
        _debounce.Stop();
        UpdateButtons();
        try
        {
            var message = await ReplaceRequested(new(files, _resultQuery, GlobalReplacement.Text ?? "", _resultOptions with { PreserveCase = GlobalPreserveCase.IsChecked == true }));
            _replacing = false;
            await SearchAsync();
            var previous = _summary;
            SetSummary(() => message + "\n" + previous());
        }
        catch (Exception ex) { SetSummary(() => ex.Message); }
        finally { _replacing = false; UpdateButtons(); }
    }
}
