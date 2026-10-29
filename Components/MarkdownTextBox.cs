using Avalonia;
using Avalonia.Controls;

namespace Mdhub.Components;

// Keeps TextBox's editing, undo, selection, and IME support; only its text presenter adds colors.
public sealed class MarkdownTextBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);
    public static readonly StyledProperty<bool> HighlightMarkdownProperty = AvaloniaProperty.Register<MarkdownTextBox, bool>(nameof(HighlightMarkdown));
    public bool HighlightMarkdown { get => GetValue(HighlightMarkdownProperty); set => SetValue(HighlightMarkdownProperty, value); }
}
