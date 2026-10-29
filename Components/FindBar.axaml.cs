using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Mdhub.Text;

namespace Mdhub.Components;

// VS Code-style find/replace bar over the text editor. Matching rules live in Text/FindReplace.
public partial class FindBar : UserControl
{
    private static readonly Geometry Collapsed = Geometry.Parse("M 6,4 L 10,8 6,12");
    private static readonly Geometry Expanded = Geometry.Parse("M 4,6 L 8,10 12,6");
    private TextBox? _editor;
    private Func<(int Start, int End)>? _selection;
    private IReadOnlyList<FindMatch> _matches = [];
    private int _current = -1;
    private (int Start, int End)? _range;
    private (int Start, int End) _openSelection;
    private bool _applying;

    public event Action? Closed;
    public FindHighlightOverlay? Overlay { get; set; }
    public IReadOnlyList<FindMatch> Matches => _matches;
    public int CurrentIndex => _current;
    public (int Start, int End)? SearchRange => _range;
    public bool IsOpen => IsVisible;
    public bool HasError { get; private set; }

    public FindBar()
    {
        InitializeComponent();
        FindInput.TextChanged += (_, _) => Search(_editor is null ? 0 : Math.Min(_editor.SelectionStart, _editor.SelectionEnd));
        foreach (var toggle in new[] { MatchCaseToggle, WholeWordToggle, RegexToggle })
            toggle.IsCheckedChanged += (_, _) => Search(CurrentStart());
        FindInSelectionToggle.IsCheckedChanged += (_, _) => ToggleInSelection();
        ExpandReplaceToggle.IsCheckedChanged += (_, _) => SetExpanded(ExpandReplaceToggle.IsChecked == true);
        FindPreviousButton.Click += (_, _) => Previous();
        FindNextButton.Click += (_, _) => Next();
        FindCloseButton.Click += (_, _) => Close();
        ReplaceButton.Click += (_, _) => Replace();
        ReplaceAllButton.Click += (_, _) => ReplaceAll();
        FindInput.AddHandler(KeyDownEvent, OnFindKey, RoutingStrategies.Tunnel);
        ReplaceInput.AddHandler(KeyDownEvent, OnReplaceKey, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } });
    }

    private FindOptions Options => new(MatchCaseToggle.IsChecked == true, WholeWordToggle.IsChecked == true,
        RegexToggle.IsChecked == true, PreserveCaseToggle.IsChecked == true);

    // selection: the editor's remembered selection, which survives the focus moving into the bar.
    public void Attach(TextBox editor, Func<(int Start, int End)> selection)
    {
        if (_editor is not null) _editor.TextChanged -= OnEditorTextChanged;
        _editor = editor;
        _selection = selection;
        _range = null;
        FindInSelectionToggle.IsChecked = false;
        editor.TextChanged += OnEditorTextChanged;
        if (IsVisible) Search(CurrentStart(), select: false); else UpdateView();
    }

    public void Open(bool expandReplace)
    {
        if (_editor is null) return;
        var (start, end) = Selection();
        _openSelection = (start, end);
        var selected = end > start ? (_editor.Text ?? "")[start..end] : "";
        if (expandReplace) ExpandReplaceToggle.IsChecked = true;
        IsVisible = true;
        if (selected.Length > 0 && !selected.Contains('\n') && !selected.Contains('\r') && selected != FindInput.Text)
            FindInput.Text = selected; // TextChanged searches from the selection start
        else Search(start);
        FocusFind();
        // A bar that was just shown is not laid out yet, so focus again once layout has run.
        Avalonia.Threading.Dispatcher.UIThread.Post(FocusFind, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void FocusFind()
    {
        if (!IsVisible) return;
        FindInput.Focus();
        FindInput.SelectAll();
    }

    public void Close()
    {
        if (!IsVisible) return;
        IsVisible = false;
        Overlay?.Update(null, [], -1);
        Closed?.Invoke();
        // The TextBox drops its selection while the bar has focus; leave the current match selected.
        if (_editor is not null && _current >= 0 && _current < _matches.Count && _matches[_current].End <= (_editor.Text?.Length ?? 0))
        {
            _editor.SelectionStart = _matches[_current].Start;
            _editor.SelectionEnd = _matches[_current].End;
        }
    }

    public void Next() => Step(forward: true);
    public void Previous() => Step(forward: false);

    private void Step(bool forward)
    {
        if (_matches.Count == 0) return;
        if (_current < 0)
        {
            var caret = _editor is null ? 0 : _editor.SelectionEnd;
            _current = forward ? FindReplace.NextIndex(_matches, caret) : FindReplace.PreviousIndex(_matches, Math.Min(_editor?.SelectionStart ?? 0, caret));
        }
        else _current = (_current + (forward ? 1 : _matches.Count - 1)) % _matches.Count;
        SelectCurrent();
    }

    public void Replace()
    {
        if (_editor is null || _editor.IsReadOnly || _current < 0 || _current >= _matches.Count) return;
        var text = _editor.Text ?? "";
        var match = _matches[_current];
        var replacement = FindReplace.Replacement(text, match, FindInput.Text ?? "", ReplaceInput.Text ?? "", Options);
        Apply(match.Start, match.End, replacement);
        Recompute();
        _current = FindReplace.NextIndex(_matches, match.Start + replacement.Length);
        SelectCurrent();
    }

    public void ReplaceAll()
    {
        if (_editor is null || _editor.IsReadOnly) return;
        var result = FindReplace.ReplaceAll(_editor.Text ?? "", FindInput.Text ?? "", ReplaceInput.Text ?? "", Options, _range);
        if (result.Count == 0) return;
        Apply(result.Start, result.End, result.Segment);
        Recompute();
        _current = -1;
        UpdateView();
    }

    // One SelectedText assignment is one undo step; the search range follows the length change.
    private void Apply(int start, int end, string replacement)
    {
        var editor = _editor!;
        _applying = true;
        try
        {
            editor.SelectionStart = start;
            editor.SelectionEnd = end;
            editor.SelectedText = replacement;
        }
        finally { _applying = false; }
        if (_range is { } r) _range = (r.Start, r.End + replacement.Length - (end - start));
    }

    private void OnEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_applying || !IsVisible) return;
        var anchor = _current >= 0 && _current < _matches.Count ? _matches[_current].Start : CurrentStart();
        Recompute();
        _current = _matches.Count == 0 ? -1 : FindReplace.NextIndex(_matches, anchor);
        UpdateView();
    }

    private void Search(int anchor, bool select = true)
    {
        if (!IsVisible) return;
        Recompute();
        _current = FindReplace.NextIndex(_matches, anchor);
        if (select && _current >= 0) SelectCurrent(); else UpdateView();
    }

    private void Recompute()
    {
        var text = _editor?.Text ?? "";
        if (_range is { } r) _range = (Math.Clamp(r.Start, 0, text.Length), Math.Clamp(r.End, 0, text.Length));
        var result = FindReplace.FindAll(text, FindInput.Text ?? "", Options, _range);
        _matches = result.Matches;
        HasError = !result.IsValid;
        FindInput.Classes.Set("invalid", HasError);
    }

    private void SelectCurrent()
    {
        if (_editor is not null && _current >= 0 && _current < _matches.Count)
        {
            var match = _matches[_current];
            _editor.SelectionStart = match.Start;
            _editor.SelectionEnd = match.End;
            var text = _editor.Text ?? "";
            var line = 0;
            for (var i = 0; i < match.Start && i < text.Length; i++) if (text[i] == '\n') line++;
            _editor.ScrollToLine(line);
        }
        UpdateView();
    }

    private void UpdateView()
    {
        FindCount.Text = string.IsNullOrEmpty(FindInput.Text) ? "No results" : FindReplace.CountLabel(_current, _matches.Count);
        FindCount.Foreground = _matches.Count == 0 && !string.IsNullOrEmpty(FindInput.Text) ? Brush.Parse("#E8A08D") : Brush.Parse("#AAB8CC");
        var any = _matches.Count > 0;
        FindPreviousButton.IsEnabled = FindNextButton.IsEnabled = any;
        ReplaceButton.IsEnabled = ReplaceAllButton.IsEnabled = any;
        Overlay?.Update(IsVisible ? _editor : null, _matches, _current);
    }

    private void ToggleInSelection()
    {
        if (FindInSelectionToggle.IsChecked == true && _editor is not null)
        {
            var (start, end) = Selection();
            // If the selection is just the current match, use the selection from when the bar opened.
            if (_current >= 0 && _current < _matches.Count && _matches[_current].Start == start && _matches[_current].End == end) (start, end) = _openSelection;
            if (end <= start) (start, end) = LineAt(start);
            _range = (start, end);
        }
        else _range = null;
        Search(_range?.Start ?? CurrentStart(), select: false);
    }

    private (int, int) LineAt(int position)
    {
        var text = _editor?.Text ?? "";
        position = Math.Clamp(position, 0, text.Length);
        var start = position == 0 ? 0 : text.LastIndexOf('\n', position - 1) + 1;
        var end = text.IndexOf('\n', position);
        return (start, end < 0 ? text.Length : end);
    }

    private (int Start, int End) Selection()
    {
        var (a, b) = _editor is { } e && e.SelectionStart != e.SelectionEnd ? (e.SelectionStart, e.SelectionEnd) : _selection?.Invoke() ?? (0, 0);
        var length = _editor?.Text?.Length ?? 0;
        return (Math.Clamp(Math.Min(a, b), 0, length), Math.Clamp(Math.Max(a, b), 0, length));
    }

    private int CurrentStart() => _editor is null ? 0 : Math.Min(_editor.SelectionStart, _editor.SelectionEnd);

    private void SetExpanded(bool expanded)
    {
        ReplaceRow.IsVisible = expanded;
        ExpandIcon.Data = expanded ? Expanded : Collapsed;
        if (!expanded && ReplaceInput.IsFocused) FindInput.Focus();
    }

    private void OnFindKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.F3)
        {
            e.Handled = true;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) Previous(); else Next();
        }
    }

    private void OnReplaceKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) ReplaceAll(); else Replace();
    }
}