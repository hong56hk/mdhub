using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Mdhub.Models;
using Mdhub.Text;
using Mdhub.Localization;

namespace Mdhub.Components;

public partial class BottomStatusBar : UserControl
{
    public BottomStatusBar() => InitializeComponent();

    // Item 40: the path shown in the status bar (null for untitled notes or while a message is shown).
    public string? CopyablePath { get; private set; }
    public static TimeSpan ConfirmationDuration { get; set; } = TimeSpan.FromSeconds(1.5);
    private DispatcherTimer? _confirmation;

    private void SetPath(string? path)
    {
        _confirmation?.Stop();
        CopyablePath = path;
        Status.Classes.Set("pathLink", path is not null);
        ToolTip.SetTip(Status, path is null ? null : Loc.T("CopyPathTip"));
    }

    private async void Status_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left) await CopyPathAsync();
    }

    // Copies the full path and briefly confirms it in place of the path; untitled notes do nothing.
    public async Task<bool> CopyPathAsync()
    {
        if (CopyablePath is not { } path || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return false;
        await clipboard.SetTextAsync(path);
        var shown = Status.Text;
        Status.Text = Loc.T("PathCopied");
        _confirmation?.Stop();
        _confirmation = new DispatcherTimer { Interval = ConfirmationDuration };
        _confirmation.Tick += (_, _) =>
        {
            _confirmation?.Stop();
            if (Status.Text == Loc.T("PathCopied") && CopyablePath == path) Status.Text = shown;
        };
        _confirmation.Start();
        return true;
    }

    public void UpdateDocument(NoteDocument document, string? message = null)
    {
        SetPath(message is null ? document.Path : null);
        Status.Text = message ?? (document.IsDirty ? Loc.T("UnsavedPrefix") : "") + (document.Path ?? Loc.T("NewNote"));
        FileKindLabel.Text = Loc.T(document.IsImage ? "KindImage" : document.IsMarkdown ? "KindMarkdown" : "KindPlain");
        EncodingLabel.Text = document.IsImage ? "" : document.Encoding.WebName.ToUpperInvariant();
        if (document.Image is { } image) SetCounts(() => Loc.F("ImagePixels", image.PixelSize.Width, image.PixelSize.Height));
    }
    public void SetMessage(string message) { SetPath(null); Status.Text = message; }
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
