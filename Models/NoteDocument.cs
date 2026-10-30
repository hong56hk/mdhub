using Mdhub.Services;
using System.ComponentModel;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Mdhub.Models;

public sealed class NoteDocument : INotifyPropertyChanged
{
    public string? Path { get; set; }
    public string UntitledName { get; init; } = "Untitled.md";
    public string SavedText { get; set; } = "";
    public Encoding Encoding { get; set; } = TextFileStore.DefaultEncoding;
    public Avalonia.Media.Imaging.Bitmap? Image { get; init; }
    public bool IsImage => Image is not null;
    public TextBox Editor { get; } = new Mdhub.Components.MarkdownTextBox()
    {
        Classes = { "noteEditor" },
        AcceptsReturn = true, AcceptsTab = true,
        FontFamily = new FontFamily("Consolas, Menlo, monospace"), FontSize = 15,
        Padding = new Thickness(18), BorderThickness = new Thickness(0),
        Background = Brushes.Transparent, TextWrapping = TextWrapping.NoWrap,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
    };
    public string Text => Editor.Text ?? "";
    public bool IsDirty => Text != SavedText;
    public bool IsMarkdown => !IsImage && System.IO.Path.GetExtension(Path ?? UntitledName).ToLowerInvariant() is ".md" or ".markdown";
    public string Title => (IsDirty ? "● " : "") + (Path is null ? UntitledName : System.IO.Path.GetFileName(Path));
    public event PropertyChangedEventHandler? PropertyChanged;

    public NoteDocument()
    {
        // Transparent so the current-line band behind the text shows; the editor pane paints EditorBackground already.
        Editor.Resources["TextControlBackgroundFocused"] = Brushes.Transparent;
        Editor.Resources["TextControlBackgroundPointerOver"] = Brushes.Transparent;
        Editor.Resources["TextControlBorderBrushFocused"] = Mdhub.Services.ThemeService.Brush("BorderSubtle");
        ScrollViewer.SetHorizontalScrollBarVisibility(Editor, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(Editor, ScrollBarVisibility.Auto);
        // A horizontal scroll viewer gives the text unlimited width, so wrapping needs it disabled.
        Editor.PropertyChanged += (_, e) =>
        {
            // Remember the selection, but ignore the collapse the TextBox does when it loses focus or leaves the tab.
            if ((e.Property == TextBox.SelectionStartProperty || e.Property == TextBox.SelectionEndProperty) && !_restoring
                && (Editor.IsFocused || Editor.SelectionStart != Editor.SelectionEnd))
                SavedSelection = (Editor.SelectionStart, Editor.SelectionEnd);
            if (e.Property == TextBox.TextWrappingProperty)
                ScrollViewer.SetHorizontalScrollBarVisibility(Editor, Editor.TextWrapping == TextWrapping.NoWrap ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled);
        };
    }

    private bool _restoring;
    public (int Start, int End) SavedSelection { get; private set; }
    public void RestoreSelection()
    {
        var length = Text.Length;
        var (start, end) = SavedSelection;
        _restoring = true;
        try
        {
            Editor.SelectionStart = Math.Clamp(start, 0, length);
            Editor.SelectionEnd = Math.Clamp(end, 0, length);
        }
        finally { _restoring = false; }
    }

    public void Refresh()
    {
        ((Mdhub.Components.MarkdownTextBox)Editor).HighlightMarkdown = IsMarkdown;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
    }
}
