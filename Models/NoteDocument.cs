using Mdhub.Services;
using System.ComponentModel;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Mdhub.Models;

public sealed class NoteDocument : INotifyPropertyChanged, IDisposable
{
    // A document is a view. Buffer owns file identity and content shared by every view.
    public NoteBuffer Buffer { get; }
    public string? Path { get => Buffer.Path; set { Buffer.Path = value; Buffer.Notify(); } }
    public string UntitledName { get => Buffer.UntitledName; init => Buffer.UntitledName = value; }
    public string SavedText { get => Buffer.SavedText; set { Buffer.SavedText = value; Buffer.Notify(); } }
    public Encoding Encoding { get => Buffer.Encoding; set { Buffer.Encoding = value; Buffer.Notify(); } }
    public Avalonia.Media.Imaging.Bitmap? Image { get => Buffer.Image; init => Buffer.Image = value; }
    public Mdhub.Services.PdfDocument? Pdf { get => Buffer.Pdf; init => Buffer.Pdf = value; }
    public bool IsImage => Image is not null;
    public bool IsPdf => Pdf is not null;
    public bool IsBinary { get => Buffer.IsBinary; init => Buffer.IsBinary = value; }
    public bool IsViewOnly => IsImage || IsPdf || IsBinary;
    public bool IsReadOnlyText { get => Buffer.IsReadOnlyText; init => Buffer.IsReadOnlyText = value; }
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
    public string Text => Buffer.Text;
    // Item 68: the line ending Enter, paste and Change End of Line Sequence use (detected on open: the majority);
    // switching it marks the document modified even when the text has no line break yet.
    public Mdhub.Text.LineEnding LineEnding { get => Buffer.LineEnding; set { Buffer.LineEnding = value; Buffer.Notify(); } }
    public Mdhub.Text.LineEnding SavedLineEnding { get => Buffer.SavedLineEnding; set { Buffer.SavedLineEnding = value; Buffer.Notify(); } }
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
    public bool IsHtml => !IsViewOnly && System.IO.Path.GetExtension(Path ?? UntitledName).ToLowerInvariant() is ".html" or ".htm";
    public bool HasPreview => IsMarkdown || IsHtml;
    // Item 78: the name the file-type icon is looked up by.
    public string IconName => Path ?? UntitledName;
    public string Title => (IsDirty ? "● " : "") + (Path is null ? UntitledName : System.IO.Path.GetFileName(Path));
    public event PropertyChangedEventHandler? PropertyChanged;

    public NoteDocument() : this(new NoteBuffer()) { }
    public NoteDocument(NoteBuffer buffer)
    {
        Buffer = buffer;
        Buffer.Attach();
        Editor.Text = buffer.Text;
        Editor.NewLine = Mdhub.Text.LineEndings.Sequence(buffer.LineEnding);
        Buffer.Changed += OnBufferChanged;
        Editor.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty && !_receiving)
                Buffer.UpdateText(Editor.Text ?? "", this);
        };
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

    private bool _receiving;
    private void OnBufferChanged(NoteDocument? source)
    {
        Editor.NewLine = Mdhub.Text.LineEndings.Sequence(Buffer.LineEnding);
        if (source != this && Editor.Text != Buffer.Text)
        {
            var old = Editor.Text ?? "";
            var next = Buffer.Text;
            var prefix = 0;
            while (prefix < old.Length && prefix < next.Length && old[prefix] == next[prefix]) prefix++;
            var suffix = 0;
            while (suffix < old.Length - prefix && suffix < next.Length - prefix && old[^(suffix + 1)] == next[^(suffix + 1)]) suffix++;
            int Map(int offset) => offset <= prefix ? offset : offset >= old.Length - suffix
                ? offset + next.Length - old.Length : prefix + next.Length - prefix - suffix;
            var start = Map(Editor.SelectionStart);
            var end = Map(Editor.SelectionEnd);
            _receiving = true;
            try
            {
                Editor.Text = next;
                Editor.SelectionStart = Math.Clamp(start, 0, next.Length);
                Editor.SelectionEnd = Math.Clamp(end, 0, next.Length);
                SavedSelection = (Editor.SelectionStart, Editor.SelectionEnd);
            }
            finally { _receiving = false; }
        }
        Refresh();
    }
    public bool CanUndo => Buffer.HasSharedHistory ? Buffer.CanUndo : Editor.CanUndo;
    public bool CanRedo => Buffer.HasSharedHistory ? Buffer.CanRedo : Editor.CanRedo;
    public void Undo() { if (Editor.IsReadOnly) return; if (Buffer.HasSharedHistory) Buffer.Undo(); else Editor.Undo(); }
    public void Redo() { if (Editor.IsReadOnly) return; if (Buffer.HasSharedHistory) Buffer.Redo(); else Editor.Redo(); }
    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Buffer.Changed -= OnBufferChanged;
        Buffer.Detach();
    }

    private bool _restoring;
    public (int Start, int End) SavedSelection { get; private set; }
    internal void RememberSelection() => SavedSelection = (Editor.SelectionStart, Editor.SelectionEnd);
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
        var box = (Mdhub.Components.MarkdownTextBox)Editor;
        var extension = System.IO.Path.GetExtension(Path ?? UntitledName);
        box.SourceLanguage = !IsViewOnly && !IsMarkdown ? Mdhub.Text.CodeHighlighter.LanguageForExtension(extension) : null;
        box.EnableJsonAutoIndent = !IsViewOnly && Mdhub.Text.JsonAutoIndent.AppliesTo(Path ?? UntitledName);
        box.StyleInlineCode = extension.Equals(".md", StringComparison.OrdinalIgnoreCase);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconName)));
    }
}
