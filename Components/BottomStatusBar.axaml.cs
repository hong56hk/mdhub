using Avalonia.Controls;
using Mdhub.Models;
using Mdhub.Text;

namespace Mdhub.Components;

public partial class BottomStatusBar : UserControl
{
    public BottomStatusBar() => InitializeComponent();
    public void UpdateDocument(NoteDocument document, string? message = null)
    {
        Status.Text = message ?? (document.IsDirty ? "Unsaved changes · " : "") + (document.Path ?? "New note");
        FileKindLabel.Text = document.IsImage ? "Image" : document.IsMarkdown ? "Markdown" : "Plain text";
        EncodingLabel.Text = document.IsImage ? "" : document.Encoding.WebName.ToUpperInvariant();
        if (document.Image is { } image) Counts.Text = $"{image.PixelSize.Width:N0} × {image.PixelSize.Height:N0} pixels";
    }
    public void SetMessage(string message) => Status.Text = message;
    public void UpdateStatistics(TextStatistics stats) =>
        Counts.Text = $"{stats.Words:N0} words    {stats.Characters:N0} characters    {stats.Lines:N0} lines";
}
