using Avalonia;
using Avalonia.Controls;

namespace Mdhub.Components;

// Keeps TextBox's editing, undo, selection, and IME support; only its text presenter adds colors.
public sealed class MarkdownTextBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);
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
        if (e.Key == Avalonia.Input.Key.Tab && !IsReadOnly && AcceptsTab && (e.KeyModifiers is Avalonia.Input.KeyModifiers.None or Avalonia.Input.KeyModifiers.Shift))
        {
            var s = Mdhub.Services.EditorBehavior.Current;
            if (Mdhub.Text.TabEditing.Apply(Text ?? "", SelectionStart, SelectionEnd, e.KeyModifiers == Avalonia.Input.KeyModifiers.Shift, s.InsertSpaces, s.TabSize) is { } edit)
                ApplyEdit(edit);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    public void ApplyEdit(Mdhub.Text.CaretEdit edit)
    {
        SelectionStart = edit.Start; SelectionEnd = edit.End;
        SelectedText = edit.Replacement;
        SelectionStart = edit.SelectionStart; SelectionEnd = edit.SelectionEnd;
        if (edit.SelectionStart == edit.SelectionEnd) CaretIndex = edit.SelectionEnd;
    }
}
