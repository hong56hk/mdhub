using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub.Localization;
using Mdhub.Services;
using Mdhub.Text;

namespace Mdhub.Components;

// Ctrl+P: VS Code-style Quick Open over the editor area. Lists recent files of the open folder with nothing typed,
// otherwise fuzzy-matches every file under it (QuickOpenRanker). "name:line" jumps to a line; ":line" alone
// jumps in the current file.
public partial class QuickOpen : UserControl
{
    private QuickOpenIndex? _index;
    private CancellationTokenSource? _query;
    private IReadOnlyList<QuickOpenItem> _items = [];
    private TopLevel? _host;
    public QuickOpenIndex? Index
    {
        get => _index;
        set { if (_index is not null) _index.Changed -= OnIndexChanged; _index = value; if (value is not null) value.Changed += OnIndexChanged; if (IsOpen) _ = RefreshAsync(); }
    }
    // Recently opened files (full paths, newest first).
    public Func<IReadOnlyList<string>>? RecentFiles { get; set; }
    // (full path or null for the current file, line or null).
    public Func<string?, int?, Task>? Accept { get; set; }
    public IReadOnlyList<QuickOpenItem> Items => _items;
    public int SelectedIndex => QuickOpenList.SelectedIndex;
    public bool IsOpen => IsVisible;
    // The latest query; tests await it.
    public Task Pending { get; private set; } = Task.CompletedTask;
    public string Text { get => QuickOpenInput.Text ?? ""; set => QuickOpenInput.Text = value; }

    public QuickOpen()
    {
        InitializeComponent();
        QuickOpenInput.TextChanged += (_, _) => { if (IsOpen) Pending = RefreshAsync(); };
        QuickOpenInput.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        QuickOpenList.AddHandler(PointerReleasedEvent, OnRowReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AttachedToVisualTree += (_, _) => Loc.Changed += OnLanguageChanged;
        DetachedFromVisualTree += (_, _) => { Loc.Changed -= OnLanguageChanged; Index = null; };
    }

    public void Open()
    {
        if (!IsOpen)
        {
            IsVisible = true;
            _host = TopLevel.GetTopLevel(this);
            _host?.AddHandler(PointerPressedEvent, OnHostPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
        // Like VS Code, Ctrl+P again keeps the text and selects it.
        // Just made visible: the input gets its template on the next layout pass, so focus again after it.
        QuickOpenInput.Focus();
        QuickOpenInput.SelectAll();
        Dispatcher.UIThread.Post(() => { if (IsOpen) { QuickOpenInput.Focus(); QuickOpenInput.SelectAll(); } }, DispatcherPriority.Loaded);
        Pending = RefreshAsync();
    }

    public void Close()
    {
        if (!IsOpen) return;
        _query?.Cancel();
        IsVisible = false;
        _host?.RemoveHandler(PointerPressedEvent, OnHostPointerPressed);
        _host = null;
        Text = "";
        Closed?.Invoke();
    }
    public event Action? Closed;

    private void OnHostPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual v && (v == this || this.IsVisualAncestorOf(v))) return;
        Close();
    }

    private void OnIndexChanged() => Dispatcher.UIThread.Post(() => { if (IsOpen) Pending = RefreshAsync(); });
    private void OnLanguageChanged() { if (IsOpen) Pending = RefreshAsync(); }

    private string Relative(string full) => _index is null ? full : Path.GetRelativePath(_index.Root, full);

    private IReadOnlyList<string> RecentInFolder()
    {
        if (_index is null || RecentFiles is null) return [];
        var root = _index.Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return RecentFiles().Where(p => p.StartsWith(root, comparison) && File.Exists(p)).Select(Relative).ToList();
    }

    private async Task RefreshAsync()
    {
        _query?.Cancel();
        var cancel = new CancellationTokenSource();
        _query = cancel;
        var (pattern, line) = QuickOpenRanker.Parse(Text);
        var recent = RecentInFolder();
        IReadOnlyList<QuickOpenItem> items = [];
        string? message = null;
        if (_index is null) message = line is not null && pattern.Length == 0 ? Loc.F("QuickOpenGoToLine", line) : Loc.T("QuickOpenNoFolder");
        else if (pattern.Length == 0)
        {
            items = line is null ? QuickOpenRanker.Rank([], "", recent) : [];
            if (line is not null) message = Loc.F("QuickOpenGoToLine", line);
            else if (items.Count == 0) message = Loc.T("QuickOpenTypeToSearch");
        }
        else
        {
            var index = _index;
            try
            {
                var files = await index.GetFilesAsync();
                if (cancel.IsCancellationRequested) return;
                items = await Task.Run(() => QuickOpenRanker.Rank(files, pattern, recent, QuickOpenRanker.MaxResults, cancel.Token), cancel.Token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { message = ex.Message; }
            if (cancel.IsCancellationRequested) return;
            if (items.Count == 0 && message is null) message = Loc.T("QuickOpenNoMatches");
        }
        Show(items, message, pattern.Length == 0 && items.Count > 0);
    }

    private void Show(IReadOnlyList<QuickOpenItem> items, string? message, bool recentHeader)
    {
        _items = items;
        QuickOpenHeader.Text = Loc.T("QuickOpenRecent");
        QuickOpenHeader.IsVisible = recentHeader;
        QuickOpenMessage.Text = message ?? "";
        QuickOpenMessage.IsVisible = message is not null;
        QuickOpenList.Items.Clear();
        foreach (var item in items) QuickOpenList.Items.Add(new ListBoxItem { Content = Row(item), Tag = item });
        QuickOpenList.IsVisible = items.Count > 0;
        QuickOpenList.SelectedIndex = items.Count > 0 ? 0 : -1;
    }

    // Name (matched letters highlighted), then the relative folder dimmed.
    private static Control Row(QuickOpenItem item)
    {
        var hits = item.Hits.ToHashSet();
        var text = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13 };
        void Add(int from, int to, IBrush? normal, double size)
        {
            for (var i = from; i < to;)
            {
                var hit = hits.Contains(i);
                var j = i;
                while (j < to && hits.Contains(j) == hit) j++;
                var run = new Run(item.RelativePath[i..j]) { FontSize = size };
                if (hit) { run.Foreground = ThemeService.Brush("SearchMatchForeground"); run.FontWeight = FontWeight.Bold; }
                else if (normal is not null) run.Foreground = normal;
                text.Inlines!.Add(run);
                i = j;
            }
        }
        Add(item.NameStart, item.RelativePath.Length, ThemeService.Brush("TextPrimary"), 13);
        if (item.Folder.Length > 0)
        {
            text.Inlines!.Add(new Run("   ") { FontSize = 12 });
            Add(0, item.NameStart - 1, ThemeService.Brush("TextMuted"), 12);
        }
        ToolTip.SetTip(text, item.RelativePath);
        // Item 78: the same file-type icon as the explorer, tabs and search results.
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = FileRowHeader.Gap };
        row.Children.Add(new FileTypeIcon(item.RelativePath, FileSupport.IsKnownBinary(item.RelativePath)));
        row.Children.Add(text);
        return row;
    }

    public void Move(int delta)
    {
        if (_items.Count == 0) return;
        var next = ((QuickOpenList.SelectedIndex < 0 ? 0 : QuickOpenList.SelectedIndex) + delta) % _items.Count;
        if (next < 0) next += _items.Count;
        QuickOpenList.SelectedIndex = next;
        QuickOpenList.ScrollIntoView(next);
    }

    private async void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: e.Handled = true; Move(1); break;
            case Key.Up: e.Handled = true; Move(-1); break;
            case Key.PageDown: e.Handled = true; Move(Math.Min(10, Math.Max(1, _items.Count - 1 - SelectedIndex))); break;
            case Key.PageUp: e.Handled = true; Move(-Math.Min(10, Math.Max(1, SelectedIndex))); break;
            case Key.Escape: e.Handled = true; Close(); break;
            case Key.Enter: e.Handled = true; await AcceptAsync(); break;
        }
    }

    private async void OnRowReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left || e.Source is not Visual v) return;
        var row = v as ListBoxItem ?? v.FindAncestorOfType<ListBoxItem>();
        if (row is null) return;
        QuickOpenList.SelectedItem = row;
        await AcceptAsync();
    }

    // Enter: the selected file (and line); with no file part, ":line" in the current file.
    public async Task AcceptAsync()
    {
        await Pending;
        var (pattern, line) = QuickOpenRanker.Parse(Text);
        string? path = null;
        if (pattern.Length > 0 && QuickOpenList.SelectedIndex >= 0 && QuickOpenList.SelectedIndex < _items.Count && _index is not null)
            path = Path.Combine(_index.Root, _items[QuickOpenList.SelectedIndex].RelativePath);
        else if (pattern.Length == 0 && line is null && QuickOpenList.SelectedIndex >= 0 && QuickOpenList.SelectedIndex < _items.Count && _index is not null)
            path = Path.Combine(_index.Root, _items[QuickOpenList.SelectedIndex].RelativePath);
        else if (pattern.Length > 0 || line is null) return;
        Close();
        if (Accept is not null) await Accept(path, line);
    }
}