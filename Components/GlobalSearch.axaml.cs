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
    private FindOptions _resultOptions;
    public WorkspaceSearchResult Result { get; private set; } = new([]);
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
    }
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
        Schedule();
        AttachedToVisualTree += (_, _) => { Loc.Changed += UpdateSummary; UpdateSummary(); };
        DetachedFromVisualTree += (_, _) => Loc.Changed -= UpdateSummary;
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
        var query = GlobalQuery.Text;
        var options = Options;
        SetSummary(() => Loc.T("Searching"));
        Result = new([]);
        GlobalResults.Items.Clear();
        UpdateButtons();
        try
        {
            var result = await WorkspaceSearch.SearchAsync(_root, query, options, OpenBuffers?.Invoke() ?? new Dictionary<string, string>(), cancellation.Token);
            if (cancellation.IsCancellationRequested || result.Cancelled) return;
            Result = result;
            _resultQuery = query;
            _resultOptions = options;
            foreach (var file in result.Files)
            {
                var relative = System.IO.Path.GetRelativePath(_root, file.Path);
                var group = new TreeViewItem { Header = $"{relative}  ({file.Hits.Count})", Tag = file, IsExpanded = true };
                ToolTip.SetTip(group, System.IO.Path.GetRelativePath(_root, file.Path));
                foreach (var hit in file.Hits)
                {
                    var label = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
                    label.Inlines!.Add(new Run($"{hit.Line}: ") { Foreground = Mdhub.Services.ThemeService.Brush("SearchLineNumberForeground") });
                    var offset = Math.Clamp(hit.Start - hit.PreviewStart, 0, hit.Preview.Length);
                    var length = Math.Min(hit.Length, hit.Preview.Length - offset);
                    label.Inlines.Add(new Run(hit.Preview[..offset]));
                    label.Inlines.Add(new Run(hit.Preview.Substring(offset, length)) { Foreground = Mdhub.Services.ThemeService.Brush("SearchMatchForeground"), Background = Mdhub.Services.ThemeService.Brush("SearchMatchBackground") });
                    label.Inlines.Add(new Run(hit.Preview[(offset + length)..]));
                    var row = new TreeViewItem { Header = label, Tag = hit };
                    ToolTip.SetTip(row, hit.Preview);
                    group.Items.Add(row);
                }
                GlobalResults.Items.Add(group);
            }
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
    private void UpdateButtons()
    {
        GlobalReplaceAll.IsEnabled = !_replacing && Result.Count > 0 && !Result.Limited && Result.Error is null;
        GlobalReplaceSelected.IsEnabled = !_replacing && Result.Count > 0 && GlobalResults.SelectedItem is TreeViewItem { Tag: SearchFile or SearchHit };
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
