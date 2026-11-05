using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Mdhub.Components;

public sealed partial class MarkdownTextBox
{
    private readonly record struct CaretRange(int Anchor, int Caret)
    {
        public int Start => Math.Min(Anchor, Caret);
        public int End => Math.Max(Anchor, Caret);
    }

    // The last range is active and is mirrored by TextBox's native selection/caret.
    private readonly List<CaretRange> _carets = [];
    private bool _updatingCarets;
    private int? _preferredColumn;
    private readonly Dictionary<CaretRange, double> _navigationColumns = [];
    public bool HasMultipleCarets => _carets.Count > 1;

    private void InitMultiCaret()
    {
        AddHandler(PointerPressedEvent, (_, _) => ClearMultiCaret(), RoutingStrategies.Tunnel);
        GotFocus += (_, _) => InvalidateCarets();
        LostFocus += (_, _) => InvalidateCarets();
        AddHandler(CopyingToClipboardEvent, (_, e) =>
        {
            if (!HasMultipleCarets) return;
            e.Handled = true;
            _ = CopyCaretsAsync(false);
        });
        AddHandler(CuttingToClipboardEvent, (_, e) =>
        {
            if (!HasMultipleCarets) return;
            e.Handled = true;
            _ = CopyCaretsAsync(true);
        });
    }

    private void InvalidateCarets() => this.GetVisualDescendants().OfType<MultiCaretOverlay>().FirstOrDefault()?.InvalidateVisual();

    private void ClearMultiCaret()
    {
        _carets.Clear();
        _preferredColumn = null;
        _navigationColumns.Clear();
        InvalidateCarets();
    }

    private void MultiCaretPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (!_updatingCarets && _carets.Count > 0 && (change.Property == TextProperty
            || change.Property == SelectionStartProperty || change.Property == SelectionEndProperty || change.Property == CaretIndexProperty)) ClearMultiCaret();
    }

    private void SyncCarets()
    {
        var updating = _updatingCarets;
        _updatingCarets = true;
        try { SelectionStart = _carets[^1].Anchor; SelectionEnd = _carets[^1].Caret; }
        finally { _updatingCarets = updating; }
        InvalidateCarets();
    }

    private static List<(int Start, int End)> LogicalLines(string text)
    {
        var lines = new List<(int, int)>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n')) continue;
            lines.Add((start, i));
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            start = i + 1;
        }
        lines.Add((start, text.Length));
        return lines;
    }

    private void AddVerticalCaret(int direction)
    {
        _navigationColumns.Clear();
        var text = Text ?? "";
        var lines = LogicalLines(text);
        var at = _carets.Count > 0 ? _carets[^1].Caret : SelectionEnd;
        var row = lines.FindIndex(l => at <= l.End);
        if (row < 0 || row + direction < 0 || row + direction >= lines.Count) return;
        var line = lines[row];
        _preferredColumn ??= StringInfo.ParseCombiningCharacters(text[line.Start..Math.Min(at, line.End)]).Length;
        var target = lines[row + direction];
        var offsets = StringInfo.ParseCombiningCharacters(text[target.Start..target.End]);
        var index = _preferredColumn.Value < offsets.Length ? target.Start + offsets[_preferredColumn.Value] : target.End;
        if (_carets.Count == 0) _carets.Add(new CaretRange(SelectionStart, SelectionEnd));
        // Reversing direction visits an existing caret without duplicating it.
        var existing = _carets.FindIndex(r => r.Caret == index);
        var range = existing >= 0 ? _carets[existing] : new CaretRange(index, index);
        if (existing >= 0) _carets.RemoveAt(existing);
        _carets.Add(range);
        _tableAnchor = null;
        SyncCarets();
    }

    private bool HandleMultiCaretKey(KeyEventArgs e)
    {
        if (e.KeyModifiers == (KeyModifiers.Alt | KeyModifiers.Shift) && e.Key is Key.Up or Key.Down)
        {
            AddVerticalCaret(e.Key == Key.Up ? -1 : 1);
            e.Handled = true;
            return true;
        }
        if (!HasMultipleCarets) return false;
        if (e.Key is Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl) return false;
        if (e.Key == Key.Escape)
        {
            var at = _carets[^1].Caret;
            ClearMultiCaret();
            SelectionStart = SelectionEnd = at;
            e.Handled = true;
            return true;
        }
        if ((e.KeyModifiers & KeyModifiers.Alt) == 0
            && e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)
        {
            MoveCarets(e);
            e.Handled = true;
            return true;
        }
        if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Back or Key.Delete or Key.Enter or Key.Tab)
        {
            if (!IsReadOnly)
            {
                if (e.Key is Key.Back or Key.Delete) ReplaceAtCarets("", delete: e.Key);
                else if (e.Key == Key.Enter && AcceptsReturn) ReplaceAtCarets(NewLine);
                else if (e.Key == Key.Tab && AcceptsTab)
                {
                    var settings = Mdhub.Services.EditorBehavior.Current;
                    ReplaceAtCarets(settings.InsertSpaces ? new string(' ', settings.TabSize) : "\t");
                }
            }
            e.Handled = true;
            return true;
        }
        if (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.C or Key.X)
        {
            _ = CopyCaretsAsync(e.Key == Key.X);
            e.Handled = true;
            return true;
        }
        // Character keys must retain the carets until the following TextInput event.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is not (Key.V or Key.Insert)) ClearMultiCaret();
        return false;
    }

    private static int Step(string text, int at, int direction)
    {
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        if (direction < 0) return boundaries.LastOrDefault(i => i < at, 0);
        return boundaries.FirstOrDefault(i => i > at, text.Length);
    }

    private void MoveCarets(KeyEventArgs e)
    {
        var extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var vertical = e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown
            && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0;
        var layout = this.GetVisualDescendants().OfType<MarkdownTextPresenter>().FirstOrDefault()?.TextLayout;
        var columns = new Dictionary<CaretRange, double>();
        _updatingCarets = true;
        try
        {
            for (var i = 0; i < _carets.Count; i++)
            {
                var range = _carets[i];
                if (vertical && layout is not null)
                {
                    var x = _navigationColumns.TryGetValue(range, out var preferred) ? preferred : layout.HitTestTextPosition(range.Caret).X;
                    var rows = layout.TextLines;
                    var row = 0;
                    double top = 0;
                    while (row < rows.Count - 1 && range.Caret >= rows[row].FirstTextSourceIndex + rows[row].Length)
                        top += rows[row++].Height;
                    var direction = e.Key is Key.Up or Key.PageUp ? -1 : 1;
                    var targetRow = Math.Clamp(row + direction, 0, rows.Count - 1);
                    if (e.Key is Key.PageUp or Key.PageDown)
                    {
                        var page = this.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.Viewport.Height ?? Bounds.Height;
                        var y = Math.Max(0, top + rows[row].Height / 2 + direction * page);
                        targetRow = 0;
                        double bottom = rows[0].Height;
                        while (targetRow < rows.Count - 1 && y >= bottom) bottom += rows[++targetRow].Height;
                    }
                    double targetTop = 0;
                    for (var r = 0; r < targetRow; r++) targetTop += rows[r].Height;
                    var targetLine = rows[targetRow];
                    var target = Math.Clamp(layout.HitTestPoint(new Point(x, targetTop + targetLine.Height / 2)).TextPosition,
                        targetLine.FirstTextSourceIndex, targetLine.FirstTextSourceIndex + targetLine.Length - targetLine.NewLineLength);
                    var verticalRange = new CaretRange(extend ? range.Anchor : target, target);
                    columns[verticalRange] = x;
                    _carets[i] = verticalRange;
                    continue;
                }
                // Start navigation at this range's active end; retain its own anchor below.
                // Native vertical navigation can otherwise use the selection's beginning.
                SelectionStart = extend || vertical ? range.Caret : range.Anchor;
                SelectionEnd = range.Caret;
                var move = new KeyEventArgs { RoutedEvent = e.RoutedEvent, Source = this, Key = e.Key,
                    PhysicalKey = e.PhysicalKey, KeyModifiers = e.KeyModifiers };
                // Reuse native navigation for words and document edges,
                // including the same Home/End selection fixes as single-caret editing.
                if (e.Key is Key.Home or Key.End && e.KeyModifiers == KeyModifiers.Shift) ExtendToLineEdge(move);
                else
                {
                    if (e.Key is Key.Home or Key.End && e.KeyModifiers == KeyModifiers.None) SelectionStart = SelectionEnd;
                    base.OnKeyDown(move);
                }
                var moved = new CaretRange(extend ? range.Anchor : SelectionStart, SelectionEnd);
                _carets[i] = moved;
            }
            _preferredColumn = null;
            MergeCarets();
            _navigationColumns.Clear();
            foreach (var range in _carets)
                if (columns.TryGetValue(range, out var x)) _navigationColumns[range] = x;
            SyncCarets();
        }
        finally { _updatingCarets = false; }
    }

    private void MergeCarets()
    {
        var active = _carets[^1];
        var merged = new List<CaretRange>();
        foreach (var range in _carets.OrderBy(r => r.Start).ThenBy(r => r.End))
        {
            if (merged.Count > 0 && (range.Start < merged[^1].End || range.Start == merged[^1].Start && range.End == merged[^1].End))
                merged[^1] = new CaretRange(merged[^1].Start, Math.Max(merged[^1].End, range.End));
            else merged.Add(range);
        }
        var primary = merged.FindIndex(r => r.Start <= active.Start && r.End >= active.End);
        if (primary >= 0) { var r = merged[primary]; merged.RemoveAt(primary); merged.Add(r.Start == active.Start && r.End == active.End ? active : r); }
        _carets.Clear();
        _carets.AddRange(merged);
    }

    private void ReplaceAtCarets(string replacement, bool distributeLines = false, Key? delete = null, bool overwrite = false)
    {
        if (IsReadOnly || !HasMultipleCarets) return;
        var text = Text ?? "";
        // Expand deletion/overwrite ranges before merging, including any shared line boundaries.
        for (var i = 0; i < _carets.Count; i++)
        {
            var range = _carets[i];
            if (range.Start != range.End) continue;
            var start = range.Caret;
            var end = start;
            if (delete == Key.Back) start = Step(text, start, -1);
            else if (delete == Key.Delete) end = Step(text, end, 1);
            else if (overwrite)
                foreach (var _ in StringInfo.ParseCombiningCharacters(replacement))
                {
                    if (end == text.Length || text[end] is '\r' or '\n') break;
                    end = Step(text, end, 1);
                }
            _carets[i] = new CaretRange(start, end);
        }
        MergeCarets();
        var active = _carets[^1];
        var ranges = _carets.OrderBy(r => r.Start).ToArray();
        var parts = distributeLines ? System.Text.RegularExpressions.Regex.Split(replacement, "\r\n|\r|\n") : [replacement];
        var builder = new System.Text.StringBuilder();
        var positions = new List<CaretRange>();
        var scanned = 0;
        var primary = 0;
        for (var i = 0; i < ranges.Length; i++)
        {
            var range = ranges[i];
            builder.Append(text, scanned, range.Start - scanned);
            builder.Append(parts.Length == ranges.Length ? parts[i] : replacement);
            positions.Add(new CaretRange(builder.Length, builder.Length));
            if (range == active) primary = i;
            scanned = range.End;
        }
        builder.Append(text, scanned, text.Length - scanned);
        var final = builder.ToString();
        _updatingCarets = true;
        try
        {
            // A single native replacement makes each multi-caret edit one undo step.
            ApplyEdit(Diff(text, final, 0, 0));
            _carets.Clear();
            var primaryRange = positions[primary];
            positions.RemoveAt(primary);
            _carets.AddRange(positions);
            _carets.Add(primaryRange);
            MergeCarets();
            _preferredColumn = null;
            _navigationColumns.Clear();
            SyncCarets();
        }
        finally { _updatingCarets = false; }
    }

    private async Task CopyCaretsAsync(bool cut)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        var text = Text ?? "";
        var ranges = _carets.ToArray();
        if (!ranges.Any(r => r.Start != r.End)) return;
        var item = new DataTransferItem();
        item.SetText(string.Join(NewLine, ranges.OrderBy(r => r.Start).Select(r => text[r.Start..r.End])));
        var data = new DataTransfer();
        data.Add(item);
        await clipboard.SetDataAsync(data);
        if (cut && !IsReadOnly && Text == text && ranges.SequenceEqual(_carets)) ReplaceAtCarets("");
    }

    internal void RenderExtraCarets(DrawingContext context, MarkdownTextPresenter presenter)
    {
        if (!IsKeyboardFocusWithin || !HasMultipleCarets) return;
        foreach (var range in _carets.SkipLast(1))
        {
            if (range.Start != range.End)
                foreach (var rect in presenter.TextLayout.HitTestTextRange(range.Start, range.End - range.Start))
                    context.FillRectangle(SelectionBrush ?? Mdhub.Services.ThemeService.Brush("EditorSelectionBackground"), rect);
            var caret = presenter.TextLayout.HitTestTextPosition(range.Caret);
            context.FillRectangle(CaretBrush ?? Foreground ?? Mdhub.Services.ThemeService.Brush("EditorForeground"), new Rect(caret.X, caret.Y, 1, caret.Height));
        }
    }
}

public sealed class MultiCaretOverlay : Control
{
    public override void Render(DrawingContext context)
    {
        if (this.FindAncestorOfType<MarkdownTextBox>() is not { } editor
            || editor.GetVisualDescendants().OfType<MarkdownTextPresenter>().FirstOrDefault() is not { } presenter
            || presenter.TranslatePoint(default, this) is not { } offset) return;
        using (context.PushTransform(Matrix.CreateTranslation(offset.X, offset.Y))) editor.RenderExtraCarets(context, presenter);
    }
}
