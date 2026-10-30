using Avalonia.Controls;
using Mdhub.Models;
using Mdhub.Text;
using Mdhub.Localization;

namespace Mdhub.Components;

public partial class BottomStatusBar : UserControl
{
    public BottomStatusBar() => InitializeComponent();
    public void UpdateDocument(NoteDocument document, string? message = null)
    {
        Status.Text = message ?? (document.IsDirty ? Loc.T("UnsavedPrefix") : "") + (document.Path ?? Loc.T("NewNote"));
        FileKindLabel.Text = Loc.T(document.IsImage ? "KindImage" : document.IsMarkdown ? "KindMarkdown" : "KindPlain");
        EncodingLabel.Text = document.IsImage ? "" : document.Encoding.WebName.ToUpperInvariant();
        if (document.Image is { } image) SetCounts(() => Loc.F("ImagePixels", image.PixelSize.Width, image.PixelSize.Height));
    }
    public void SetMessage(string message) => Status.Text = message;
    // null hides the caret position (preview focused, image or no editor).
    public void SetCaret(string? position)
    {
        CaretLabel.Text = position ?? "";
        CaretLabel.IsVisible = position is not null;
    }
    public void UpdateStatistics(TextStatistics stats) =>
        SetCounts(() => Loc.F("Statistics", stats.Words, stats.Characters, stats.Lines));
    // Kept as a function so a language switch can re-render the counts.
    private Func<string>? _counts;
    private void SetCounts(Func<string> counts) { _counts = counts; Counts.Text = counts(); }
    public void Relocalize() { if (_counts is not null) Counts.Text = _counts(); }
}
