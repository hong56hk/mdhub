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
}
