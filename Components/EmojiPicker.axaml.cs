using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Media;
using System.Globalization;
using System.Text;
using Mdhub.Localization;
using Mdhub.Services;

namespace Mdhub.Components;

public partial class EmojiPicker : UserControl
{
    private static readonly (string? Id, string Icon)[] Categories =
    [
        (null, "☰"), ("Recent", "🕘"), ("Smileys", "😀"), ("People", "👋"), ("Nature", "🌿"), ("Food", "☕"),
        ("Travel", "✈️"), ("Activities", "⚽"), ("Objects", "💡"), ("Symbols", "❤️"), ("Flags", "🏳️")
    ];
    private string? _category;
    private readonly Dictionary<int, bool> _glyphs = [];
    public event Action<string>? EmojiSelected;
    // Item 92: Enter on the highlighted emoji inserts it and closes the picker.
    public event Action<string>? EmojiCommitted;
    public event Action? DismissRequested;
    private const int Columns = 9;
    // Item 92: keyboard model. Each category starts a new grid row (padded with empty cells), so rows are per category.
    private readonly List<Button> _cells = [];
    private readonly List<List<int>> _rows = [];
    private readonly List<(int Row, int Col)> _position = [];
    private int _highlight = -1;
    internal int HighlightIndex => _highlight;
    internal Button? HighlightedButton => _highlight >= 0 ? _cells[_highlight] : null;

    public EmojiPicker()
    {
        InitializeComponent();
        foreach (var category in Categories)
        {
            var button = new ToggleButton { Name = "EmojiCategory" + (category.Id ?? "All"), Content = category.Icon, Tag = category.Id };
            button.Classes.Add("emojiCategory");
            button.Click += (_, _) => { _category = category.Id; Refresh(); };
            EmojiCategories.Children.Add(button);
        }
        EmojiSearchBox.TextChanged += (_, _) => Refresh();
        AddHandler(KeyDownEvent, OnPickerKeyDown, RoutingStrategies.Tunnel);
        EmojiScroll.AddHandler(TextInputEvent, (_, e) =>
        {
            // Item 92: typing while the grid has focus continues the search.
            if (string.IsNullOrEmpty(e.Text) || e.Text.Any(char.IsControl)) return;
            e.Handled = true;
            TypeIntoSearch(EmojiSearchBox.Text + e.Text);
        }, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => { Loc.Changed += Refresh; EmojiRecents.Changed += RefreshRecents; };
        DetachedFromVisualTree += (_, _) => { Loc.Changed -= Refresh; EmojiRecents.Changed -= RefreshRecents; };
        Refresh();
    }

    public void FocusSearch() => Dispatcher.UIThread.Post(() => EmojiSearchBox.Focus(), DispatcherPriority.Loaded);

    private void Refresh()
    {
        foreach (var button in EmojiCategories.Children.OfType<ToggleButton>())
        {
            var id = button.Tag as string;
            button.IsChecked = id == _category;
            var label = Loc.T("EmojiCategory" + (id ?? "All"));
            ToolTip.SetTip(button, label);
            AutomationProperties.SetName(button, label);
        }
        var results = EmojiCatalog.Search(EmojiSearchBox.Text, _category == "Recent" ? null : _category);
        if (_category == "Recent")
        {
            var matching = results.ToDictionary(e => e.Value, StringComparer.Ordinal);
            results = EmojiRecents.Values.Where(matching.ContainsKey).Select(value => matching[value]).ToList();
        }
        var matches = results.Where(CanDisplay).ToList();
        EmojiCategoryHeading.Text = $"{Loc.T("EmojiCategory" + (_category ?? "All"))} · {matches.Count}";
        EmojiGrid.Children.Clear();
        _cells.Clear(); _rows.Clear(); _position.Clear(); _highlight = -1;
        // In All (and search across all), each category starts on a fresh row so Up/Down cross category boundaries.
        var groups = _category is null ? matches.GroupBy(e => e.Category).Select(g => g.ToList()).ToList() : [matches];
        foreach (var group in groups)
        {
            for (var i = 0; i < group.Count; i++)
            {
                if (i % Columns == 0) _rows.Add([]);
                var emoji = group[i];
                var index = _cells.Count;
                var button = new Button { Content = emoji.Value, Tag = emoji };
                button.Classes.Add("emojiCell");
                ToolTip.SetTip(button, emoji.Name);
                AutomationProperties.SetName(button, emoji.Name);
                button.Click += (_, _) => { SetHighlight(index, focus: false, scroll: false); EmojiSelected?.Invoke(emoji.Value); };
                button.PointerEntered += (_, _) => SetHighlight(index, focus: EmojiScroll.IsKeyboardFocusWithin, scroll: false);
                button.GotFocus += (_, _) => { if (_highlight != index) SetHighlight(index, focus: false, scroll: true); };
                _position.Add((_rows.Count - 1, _rows[^1].Count));
                _rows[^1].Add(index);
                _cells.Add(button);
                EmojiGrid.Children.Add(button);
            }
            if (groups.Count > 1)
                for (var pad = group.Count % Columns; pad is > 0 and < Columns; pad++) EmojiGrid.Children.Add(new Border { IsHitTestVisible = false });
        }
        if (_cells.Count > 0) SetHighlight(0, focus: false, scroll: false);
        else EmojiFooter.Text = "";
        EmojiScroll.Offset = default;
        EmojiScroll.IsVisible = matches.Count > 0;
        EmojiNoResults.IsVisible = matches.Count == 0;
        EmojiNoResults.Text = Loc.T(_category == "Recent" && EmojiRecents.Values.Count == 0 ? "EmojiNoRecents" : "EmojiNoResults");
    }

    private void SetHighlight(int index, bool focus, bool scroll)
    {
        if (index < 0 || index >= _cells.Count) return;
        if (_highlight >= 0 && _highlight < _cells.Count) _cells[_highlight].Classes.Remove("highlighted");
        _highlight = index;
        var cell = _cells[index];
        cell.Classes.Add("highlighted");
        EmojiFooter.Text = ((EmojiEntry)cell.Tag!).Name;
        if (focus) cell.Focus(NavigationMethod.Directional);
        if (scroll) cell.BringIntoView();
    }

    private void Move(int row, int col) => SetHighlight(_rows[row][Math.Clamp(col, 0, _rows[row].Count - 1)], focus: true, scroll: true);

    private void TypeIntoSearch(string text)
    {
        EmojiSearchBox.Text = text;
        EmojiSearchBox.Focus();
        EmojiSearchBox.CaretIndex = text.Length;
    }

    private void OnPickerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; DismissRequested?.Invoke(); return; }
        var inGrid = EmojiScroll.IsKeyboardFocusWithin;
        if (e.Key == Key.Enter && (inGrid || EmojiSearchBox.IsKeyboardFocusWithin))
        {
            e.Handled = true; // also stops the focused Button from clicking (which keeps the picker open)
            if (HighlightedButton?.Tag is EmojiEntry entry) EmojiCommitted?.Invoke(entry.Value);
            return;
        }
        if (EmojiSearchBox.IsKeyboardFocusWithin)
        {
            if (e.Key == Key.Down && _cells.Count > 0) { e.Handled = true; SetHighlight(Math.Max(_highlight, 0), focus: true, scroll: true); }
            return;
        }
        if (!inGrid || _highlight < 0 || e.KeyModifiers != KeyModifiers.None) return;
        var (row, col) = _position[_highlight];
        var page = Math.Max(1, (int)Math.Floor(EmojiScroll.Viewport.Height / Math.Max(1, _cells[0].Bounds.Height)));
        switch (e.Key)
        {
            case Key.Right:
                if (col + 1 < _rows[row].Count) Move(row, col + 1); else if (row + 1 < _rows.Count) Move(row + 1, 0);
                break;
            case Key.Left:
                if (col > 0) Move(row, col - 1); else if (row > 0) Move(row - 1, _rows[row - 1].Count - 1);
                break;
            case Key.Down:
                if (row + 1 < _rows.Count) Move(row + 1, col);
                break;
            case Key.Up:
                if (row > 0) Move(row - 1, col); else EmojiSearchBox.Focus();
                break;
            case Key.Home: SetHighlight(0, focus: true, scroll: true); break;
            case Key.End: SetHighlight(_cells.Count - 1, focus: true, scroll: true); break;
            case Key.PageDown: Move(Math.Min(row + page, _rows.Count - 1), col); break;
            case Key.PageUp: Move(Math.Max(row - page, 0), col); break;
            case Key.Back:
                if (!string.IsNullOrEmpty(EmojiSearchBox.Text)) TypeIntoSearch(EmojiSearchBox.Text[..^1]);
                break;
            default: return;
        }
        e.Handled = true;
    }

    private void RefreshRecents()
    {
        if (_category != "Recent") return;
        var focused = IsKeyboardFocusWithin;
        Refresh();
        if (focused) EmojiSearchBox.Focus();
    }

    private bool CanDisplay(EmojiEntry emoji)
    {
        foreach (var rune in emoji.Value.EnumerateRunes())
        {
            // Joiners and presentation selectors shape an emoji rather than supplying visible glyphs.
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Format or UnicodeCategory.NonSpacingMark) continue;
            if (!_glyphs.TryGetValue(rune.Value, out var supported))
                _glyphs[rune.Value] = supported = FontManager.Current.TryMatchCharacter(rune.Value,
                    FontStyle.Normal, FontWeight.Normal, FontStretch.Normal, FontFamily, CultureInfo.CurrentCulture, out _);
            if (!supported) return false;
        }
        return true;
    }
}
