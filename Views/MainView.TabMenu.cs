using Avalonia.Controls;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;

namespace Mdhub.Views;

// Item 75: right-click a file tab for Close tab / Close other tabs (dirty tabs go through the unsaved-changes prompt).
public partial class MainView
{
    private ContextMenu CreateTabMenu(EditorPane pane)
    {
        var menu = new ContextMenu();
        var close = LocResources.Item("CloseTab");
        close.Name = "TabCloseMenuItem";
        ShortcutKeys.SetId(close, "file.closeTab");
        close.Click += async (_, _) => { if (pane.FileTabs.MenuTarget is { } d) { ActivatePane(pane); await RunAsync(() => CloseDocumentAsync(d)); } };
        var others = LocResources.Item("CloseOtherTabs");
        others.Name = "TabCloseOthersMenuItem";
        others.Click += async (_, _) => { if (pane.FileTabs.MenuTarget is { } d) { ActivatePane(pane); await RunAsync(() => CloseOtherDocumentsAsync(d)); } };
        var split = LocResources.Item("SplitRight");
        split.Name = "TabSplitRightMenuItem";
        ShortcutKeys.SetId(split, "view.splitEditor");
        split.Click += async (_, _) =>
        {
            if (_busy || pane.FileTabs.MenuTarget is not { } document) return;
            ActivatePane(pane);
            pane.FileTabs.SelectedDocument = document;
            await RunAsync(() => OpenToSideAsync());
        };
        menu.Items.Add(close);
        menu.Items.Add(others);
        menu.Items.Add(split);
        // Opening covers a right-click; Opened also covers a menu opened from code or the keyboard.
        menu.Opening += (_, _) => others.IsEnabled = pane.Documents.Count > 1;
        menu.Opened += (_, _) => others.IsEnabled = pane.Documents.Count > 1;
        ShortcutKeys.ApplyGestures([close, split]);
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
            // Bug 79: only a dirty tab is shown (for its prompt); keep is active again before anything is removed.
            if (document.IsDirty) FileTabs.SelectedDocument = document;
            var close = AllDocuments.Any(d => d != document && d.Buffer == document.Buffer) || await ConfirmCloseAsync(document);
            FileTabs.SelectedDocument = keep;
            if (!close) return;
            RememberClosedTab(document);
            RemoveDocument(document);
            await FlushRecoveryAsync();
        }
    }
}
