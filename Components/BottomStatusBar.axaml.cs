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
        EncodingLabel.Text = document.Encoding.WebName.ToUpperInvariant();
    }
    public void SetMessage(string message) => Status.Text = message;
    public void UpdateStatistics(TextStatistics stats) =>
        Counts.Text = $"{stats.Words:N0} words    {stats.Characters:N0} characters    {stats.Lines:N0} lines";
}
