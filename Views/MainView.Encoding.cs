using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;

namespace Mdhub.Views;

// Item 67 (VS Code): the status bar encoding opens 'Reopen with Encoding' (decode the file on disk again) and
// 'Save with Encoding' (convert); saving never silently drops characters the encoding cannot represent.
public partial class MainView
{
    private void WireEncodingMenu() =>
        BottomStatusBar.EncodingRequested += target => { if (_active is { IsViewOnly: false } && !_busy) BuildEncodingMenu(_active).ShowAt(target); };

    internal MenuFlyout BuildEncodingMenu(NoteDocument document)
    {
        var current = TextEncodings.OptionOf(document.Encoding);
        MenuItem Sub(string name, string key, bool enabled, Func<EncodingOption, Task> pick)
        {
            var item = new MenuItem { Name = name, Header = Loc.T(key), IsEnabled = enabled };
            foreach (var option in TextEncodings.All)
            {
                var choice = new MenuItem { Header = option.Name, Tag = option.Id, ToggleType = MenuItemToggleType.Radio, IsChecked = option == current };
                choice.Click += async (_, _) => await RunAsync(() => pick(option));
                item.Items.Add(choice);
            }
            return item;
        }
        var flyout = new MenuFlyout { Placement = PlacementMode.TopEdgeAlignedRight };
        flyout.Items.Add(Sub("ReopenWithEncodingItem", "ReopenWithEncoding", document.Path is not null && !document.IsReadOnlyText, o => ReopenWithEncodingAsync(document, o.Id)));
        flyout.Items.Add(Sub("SaveWithEncodingItem", "SaveWithEncoding", !document.IsReadOnlyText, o => SaveWithEncodingAsync(document, o.Id)));
        return flyout;
    }

    // Decodes the file on disk again with the chosen encoding; unsaved edits are discarded after a confirmation.
    internal async Task<bool> ReopenWithEncodingAsync(NoteDocument document, string id)
    {
        if (document.Path is not { } path || document.IsViewOnly || document.IsReadOnlyText || TextEncodings.Find(id) is not { } option) return false;
        var encoding = option.Create();
        string text;
        try { text = TextEncodings.DecodeWith(await File.ReadAllBytesAsync(path), encoding); }
        catch (System.Text.DecoderFallbackException) { UpdateStatus(Loc.F("ReopenEncodingFailed", option.Name)); return false; }
        if (document.IsDirty && !await ConfirmAsync("ReopenEncodingTitle", Loc.T("ReopenEncodingConfirm"), "ReopenDiscard")) return false;
        document.Encoding = encoding;
        document.SavedText = text;
        document.Editor.Text = text;
        document.Refresh();
        if (_active == document) MdEditor.ShowDocument(document.IsMarkdown ? document : null);
        UpdateStatus(Loc.F("ReopenedEncoding", option.Name));
        return true;
    }

    // Converts: saves the text in the chosen encoding (falls back to UTF-8 only when the user agrees).
    internal async Task<bool> SaveWithEncodingAsync(NoteDocument document, string id)
    {
        if (document.IsViewOnly || document.IsReadOnlyText || TextEncodings.Find(id) is not { } option) return false;
        var previous = document.Encoding;
        document.Encoding = option.Create();
        bool saved;
        try { saved = await SaveAsync(document, false); }
        catch { document.Encoding = previous; throw; }
        if (!saved) { document.Encoding = previous; UpdateStatus(); }
        return saved;
    }

    // Before writing: a character the encoding cannot hold offers UTF-8 instead (false = cancel the save).
    private async Task<bool> EnsureEncodableAsync(NoteDocument document)
    {
        if (TextEncodings.FirstUnencodable(document.Text, document.Encoding) is not { } character) return true;
        if (!await ConfirmAsync("EncodingUnencodableTitle", Loc.F("EncodingUnencodable", TextEncodings.DisplayName(document.Encoding), character), "SaveAsUtf8")) return false;
        document.Encoding = TextFileStore.DefaultEncoding;
        return true;
    }
}