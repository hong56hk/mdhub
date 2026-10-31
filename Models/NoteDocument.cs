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
    // Item 52: a placeholder tab for a binary (or undecodable) file; no editor text.
    public bool IsBinary { get; init; }
    public bool IsViewOnly => IsImage || IsBinary;
    // Text opened anyway from a binary with lossy decoding: read-only so saving cannot corrupt the file.
    public bool IsReadOnlyText { get; init; }
    public TextBox Editor { get; } = new Mdhub.Components.MarkdownTextBox()
    {
        Classes = { "noteEditor" },
        AcceptsReturn = true, AcceptsTab = true,
        FontFamily = Mdhub.Services.EditorFonts.BodyFamily, FontSize = 15, // item 57: the editor gear's body font
        Padding = new Thickness(18), BorderThickness = new Thickness(0),
        Background = Brushes.Transparent, TextWrapping = TextWrapping.NoWrap,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
    };
    public string Text => Editor.Text ?? "";
    // Item 68: the line ending Enter, paste and Change End of Line Sequence use (detected on open: the majority);
    // switching it marks the document modified even when the text has no line break yet.
    private Mdhub.Text.LineEnding _lineEnding;
    public Mdhub.Text.LineEnding LineEnding { get => _lineEnding; set { _lineEnding = value; Editor.NewLine = Mdhub.Text.LineEndings.Sequence(value); } }
    public Mdhub.Text.LineEnding SavedLineEnding { get; set; }
    public void SetLoadedLineEnding(Mdhub.Text.LineEnding ending) { LineEnding = ending; SavedLineEnding = ending; }
    private string? _countedText;
    private Mdhub.Text.LineEndingCounts _counts;
    // Counted once per text snapshot (the status bar asks on every change).
    public Mdhub.Text.LineEndingCounts LineEndingCounts
    {
        get { var text = Text; if (!ReferenceEquals(text, _countedText)) { _counts = Mdhub.Text.LineEndings.Count(text); _countedText = text; } return _counts; }
    }
    public bool IsDirty => Text != SavedText || (LineEnding != SavedLineEnding && !IsViewOnly);
    public bool IsMarkdown => !IsViewOnly && System.IO.Path.GetExtension(Path ?? UntitledName).ToLowerInvariant() is ".md" or ".markdown";
    public string Title => (IsDirty ? "● " : "") + (Path is null ? UntitledName : System.IO.Path.GetFileName(Path));
    public event PropertyChangedEventHandler? PropertyChanged;

    public NoteDocument()
    {
        SetLoadedLineEnding(LineEndingSettings.ForNewFile);
        // Transparent so the current-line band behind the text shows; the editor pane paints EditorBackground already.
        Editor.Resources["TextControlBackgroundFocused"] = Brushes.Transparent;
        Editor.Resources["TextControlBackgroundPointerOver"] = Brushes.Transparent;
        Editor.Resources["TextControlBorderBrushFocused"] = Mdhub.Services.ThemeService.Brush("BorderSubtle");
        // Item 62: text and selection colours are theme keys (the editor gear can override them per theme).
        Editor.Foreground = Mdhub.Services.ThemeService.Brush("EditorForeground");
        Editor.SelectionBrush = Mdhub.Services.ThemeService.Brush("EditorSelectionBackground");
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
