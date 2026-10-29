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
    public TextBox Editor { get; } = new()
    {
        AcceptsReturn = true, AcceptsTab = true,
        FontFamily = new FontFamily("Consolas, Menlo, monospace"), FontSize = 15,
        Padding = new Thickness(18), BorderThickness = new Thickness(0),
        Background = Brushes.Transparent, TextWrapping = TextWrapping.NoWrap,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
    };
    public string Text => Editor.Text ?? "";
    public bool IsDirty => Text != SavedText;
    public bool IsMarkdown => System.IO.Path.GetExtension(Path ?? UntitledName).ToLowerInvariant() is ".md" or ".markdown";
    public string Title => (IsDirty ? "● " : "") + (Path is null ? UntitledName : System.IO.Path.GetFileName(Path));
    public event PropertyChangedEventHandler? PropertyChanged;

    public NoteDocument()
    {
        Editor.Resources["TextControlBackgroundFocused"] = Brush.Parse("#202329");
        Editor.Resources["TextControlBackgroundPointerOver"] = Brush.Parse("#202329");
        Editor.Resources["TextControlBorderBrushFocused"] = Brush.Parse("#30343B");
        ScrollViewer.SetHorizontalScrollBarVisibility(Editor, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(Editor, ScrollBarVisibility.Auto);
    }

    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
}
