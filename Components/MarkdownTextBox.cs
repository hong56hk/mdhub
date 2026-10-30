using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace Mdhub.Components;

// Keeps TextBox's editing, undo, selection, and IME support; only its text presenter adds colors.
public sealed class MarkdownTextBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);

    // Item 68: pasted text takes the document's line ending (NewLine, which Enter types too).
    public MarkdownTextBox() => AddHandler(PastingFromClipboardEvent, (_, e) => { if (IsReadOnly) return; e.Handled = true; _ = PasteNormalizedAsync(); });
    private async Task PasteNormalizedAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        if (await clipboard.TryGetTextAsync() is { Length: > 0 } text) InsertText(text);
    }
    public void InsertText(string text)
    {
        var newline = NewLine ?? System.Environment.NewLine;
        var normalized = System.Text.RegularExpressions.Regex.Replace(text, "\r\n|\r|\n", newline.Replace("$", "$$"));
        var start = Math.Min(SelectionStart, SelectionEnd);
        SelectedText = normalized;
        SelectionStart = SelectionEnd = start + normalized.Length;
        CaretIndex = start + normalized.Length;
    }
    public static readonly StyledProperty<bool> HighlightMarkdownProperty = AvaloniaProperty.Register<MarkdownTextBox, bool>(nameof(HighlightMarkdown));
    public bool HighlightMarkdown { get => GetValue(HighlightMarkdownProperty); set => SetValue(HighlightMarkdownProperty, value); }

    // Item 66: overwrite mode replaces the characters under the caret, never a line break; a selection or paste works as usual.
    protected override void OnTextInput(Avalonia.Input.TextInputEventArgs e)
    {
        var text = Text;
        if (Mdhub.Services.EditorMode.Overwrite && !IsReadOnly && !string.IsNullOrEmpty(e.Text) && SelectionStart == SelectionEnd && text is not null)
        {
            var at = Math.Clamp(CaretIndex, 0, text.Length);
            var end = at;
            foreach (var _ in e.Text.EnumerateRunes())
            {
                if (end >= text.Length || text[end] is '\r' or '\n') break;
                end += char.IsSurrogatePair(text, end) ? 2 : 1;
            }
            if (end > at) { SelectionStart = at; SelectionEnd = end; }
        }
        base.OnTextInput(e);
    }

    // Item 70: Tab / Shift+Tab follow the editor gear (spaces or a tab, tab size); a multi-line selection indents or
    // outdents every touched line. Each is one replacement, so one undo step.
    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        if (!IsReadOnly && HandleAutoComplete(e)) { e.Handled = true; return; }
        var lengthBefore = Text?.Length ?? 0;
        if (e.Key == Avalonia.Input.Key.Tab && !IsReadOnly && AcceptsTab && (e.KeyModifiers is Avalonia.Input.KeyModifiers.None or Avalonia.Input.KeyModifiers.Shift))
        {
            var s = Mdhub.Services.EditorBehavior.Current;
            if (Mdhub.Text.TabEditing.Apply(Text ?? "", SelectionStart, SelectionEnd, e.KeyModifiers == Avalonia.Input.KeyModifiers.Shift, s.InsertSpaces, s.TabSize) is { } edit)
                ApplyEdit(edit);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
        // Item 71: deleting inside a numbered list renumbers the items after it (its own undo step).
        if (HighlightMarkdown && !IsReadOnly && e.Key is Avalonia.Input.Key.Back or Avalonia.Input.Key.Delete && (Text?.Length ?? 0) < lengthBefore
            && Mdhub.Services.EditorBehavior.Current.AutoLists && Mdhub.Services.MarkdownAutoComplete.RenumberAt(Text ?? "", CaretIndex, AutoOptions) is { } renumber)
            ApplyEdit(renumber);
    }

    // ---- Item 71: Markdown list and table auto-complete (rules in Services/MarkdownAutoComplete) ----
    public static Mdhub.Services.MarkdownAutoComplete.Options AutoOptions
    {
        get { var s = Mdhub.Services.EditorBehavior.Current; return new(s.AutoLists, s.AutoTables, s.AlignTables, s.InsertSpaces, s.TabSize); }
    }

    private bool HandleAutoComplete(Avalonia.Input.KeyEventArgs e)
    {
        if (!HighlightMarkdown) return false;
        var text = Text ?? "";
        var mods = e.KeyModifiers;
        var empty = SelectionStart == SelectionEnd;
        var o = AutoOptions;
        if (e.Key == Avalonia.Input.Key.Enter && AcceptsReturn)
        {
            // Shift+Enter is always a plain newline.
            if (mods == Avalonia.Input.KeyModifiers.Shift) { SelectedText = NewLine; return true; }
            if (mods != Avalonia.Input.KeyModifiers.None || !empty) return false;
            if (Mdhub.Services.MarkdownAutoComplete.Enter(text, CaretIndex, o, NewLine) is not { } edit) return false;
            // Two undo steps: a plain newline first, then the completion, so Ctrl+Z leaves the plain newline.
            var final = text[..edit.Start] + edit.Replacement + text[edit.End..];
            SelectedText = NewLine;
            var now = Text ?? "";
            ApplyEdit(Diff(now, final, edit.SelectionStart, edit.SelectionEnd));
            return true;
        }
        if (e.Key == Avalonia.Input.Key.Tab && AcceptsTab && mods is Avalonia.Input.KeyModifiers.None or Avalonia.Input.KeyModifiers.Shift
            && Mdhub.Services.MarkdownAutoComplete.Tab(text, SelectionStart, SelectionEnd, mods == Avalonia.Input.KeyModifiers.Shift, o, NewLine) is { } tab)
        {
            ApplyEdit(tab);
            return true;
        }
        if (e.Key == Avalonia.Input.Key.Back && mods == Avalonia.Input.KeyModifiers.None && empty
            && Mdhub.Services.MarkdownAutoComplete.Backspace(text, CaretIndex, o) is { } back)
        {
            ApplyEdit(back);
            return true;
        }
        return false;
    }

    private static Mdhub.Text.CaretEdit Diff(string from, string to, int selectionStart, int selectionEnd)
    {
        var p = 0; while (p < from.Length && p < to.Length && from[p] == to[p]) p++;
        var s = 0; while (s < from.Length - p && s < to.Length - p && from[from.Length - 1 - s] == to[to.Length - 1 - s]) s++;
        return new Mdhub.Text.CaretEdit(p, from.Length - s, to.Substring(p, to.Length - s - p), selectionStart, selectionEnd);
    }

    // Auto-align: when the caret leaves a table, its pipes and column widths are aligned (one undo step).
    private int? _tableAnchor;
    private bool _aligning;
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != CaretIndexProperty || _aligning || !HighlightMarkdown || IsReadOnly) return;
        var text = Text ?? "";
        var caret = CaretIndex;
        var here = Mdhub.Services.MarkdownAutoComplete.TableAt(text, caret);
        if (_tableAnchor is { } anchor && anchor <= text.Length)
        {
            var was = Mdhub.Services.MarkdownAutoComplete.TableAt(text, anchor);
            if (was is not null && was != here)
            {
                _tableAnchor = null;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => AlignTableAt(anchor));
            }
        }
        if (here is not null) _tableAnchor = caret;
        else if (_tableAnchor is not null && _tableAnchor > text.Length) _tableAnchor = null;
    }

    private void AlignTableAt(int anchor)
    {
        var text = Text ?? "";
        if (anchor > text.Length || IsReadOnly) return;
        if (Mdhub.Services.MarkdownAutoComplete.AlignTable(text, anchor, CaretIndex, AutoOptions) is not { } edit) return;
        var range = Mdhub.Services.MarkdownAutoComplete.TableAt(text, anchor);
        if (range is { } r && CaretIndex >= r.Start && CaretIndex <= r.End) return; // the caret came back
        _aligning = true;
        try { var sel = (SelectionStart, SelectionEnd); ApplyEdit(edit); if (sel.SelectionStart != sel.SelectionEnd) { var d = edit.Replacement.Length - (edit.End - edit.Start); SelectionStart = sel.SelectionStart >= edit.End ? sel.SelectionStart + d : sel.SelectionStart; SelectionEnd = sel.SelectionEnd >= edit.End ? sel.SelectionEnd + d : sel.SelectionEnd; } }
        finally { _aligning = false; }
    }

    public void ApplyEdit(Mdhub.Text.CaretEdit edit)
    {
        if (edit.Start != edit.End || edit.Replacement.Length > 0)
        {
            SelectionStart = edit.Start; SelectionEnd = edit.End;
            SelectedText = edit.Replacement;
        }
        SelectionStart = edit.SelectionStart; SelectionEnd = edit.SelectionEnd;
        if (edit.SelectionStart == edit.SelectionEnd) CaretIndex = edit.SelectionEnd;
    }
}
