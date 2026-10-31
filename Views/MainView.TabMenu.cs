using Avalonia.Controls;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;

namespace Mdhub.Views;

// Item 75: right-click a file tab for Close tab / Close other tabs (dirty tabs go through the unsaved-changes prompt).
public partial class MainView
{
    private ContextMenu CreateTabMenu()
    {
        var menu = new ContextMenu();
        var close = LocResources.Item("CloseTab");
        close.Name = "TabCloseMenuItem";
        ShortcutKeys.SetId(close, "file.closeTab");
        close.Click += async (_, _) => { if (FileTabs.MenuTarget is { } d) await RunAsync(() => CloseDocumentAsync(d)); };
        var others = LocResources.Item("CloseOtherTabs");
        others.Name = "TabCloseOthersMenuItem";
        others.Click += async (_, _) => { if (FileTabs.MenuTarget is { } d) await RunAsync(() => CloseOtherDocumentsAsync(d)); };
        menu.Items.Add(close);
        menu.Items.Add(others);
        // Opening covers a right-click; Opened also covers a menu opened from code or the keyboard.
        menu.Opening += (_, _) => others.IsEnabled = _documents.Count > 1;
        menu.Opened += (_, _) => others.IsEnabled = _documents.Count > 1;
        ShortcutKeys.ApplyGestures([close]);
        return menu.Localized();
    }

    // Closes every tab except keep, left to right; cancelling a prompt stops there and keeps the rest open.
    private async Task CloseOtherDocumentsAsync(NoteDocument keep)
    {
        if (!_documents.Contains(keep)) return;
        FileTabs.SelectedDocument = keep;
        foreach (var document in _documents.Where(d => d != keep).ToList())
        {
            if (!_documents.Contains(document)) continue;
            FileTabs.SelectedDocument = document;
            if (!await ConfirmCloseAsync(document)) { FileTabs.SelectedDocument = keep; return; }
            _documents.Remove(document);
            document.Image?.Dispose();
            FileTabs.SelectedDocument = keep;
        }
    }
}