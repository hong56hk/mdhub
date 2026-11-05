using Avalonia.Controls;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;

namespace Mdhub.Views;

// File tab close actions use the unsaved-changes prompt for dirty tabs.
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
        var all = LocResources.Item("CloseAllTabs");
        all.Name = "TabCloseAllMenuItem";
        all.Click += async (_, _) =>
        {
            if (_busy || pane.FileTabs.MenuTarget is null) return;
            ActivatePane(pane);
            await RunAsync(() => CloseAllDocumentsAsync(pane));
        };
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
        menu.Items.Add(all);
        menu.Items.Add(split);
        // Item 89: move the tab into the split on that side (a new split when there is none).
        var moveRight = LocResources.Item("MoveToRightSplit");
        moveRight.Name = "TabMoveRightMenuItem";
        ShortcutKeys.SetId(moveRight, "view.moveTabRight"); // item 90: hint only; the click acts on the clicked tab
        moveRight.Click += (_, _) => { if (pane.FileTabs.MenuTarget is { } d) MoveTabToSide(pane, d, 1); };
        var moveLeft = LocResources.Item("MoveToLeftSplit");
        moveLeft.Name = "TabMoveLeftMenuItem";
        ShortcutKeys.SetId(moveLeft, "view.moveTabLeft");
        moveLeft.Click += (_, _) => { if (pane.FileTabs.MenuTarget is { } d) MoveTabToSide(pane, d, -1); };
        menu.Items.Add(moveRight);
        menu.Items.Add(moveLeft);
        void Refresh()
        {
            others.IsEnabled = pane.Documents.Count > 1;
            moveRight.IsVisible = moveLeft.IsVisible = CanMoveTab(pane);
        }
        // Opening covers a right-click; Opened also covers a menu opened from code or the keyboard.
        menu.Opening += (_, _) => Refresh();
        menu.Opened += (_, _) => Refresh();
        ShortcutKeys.ApplyGestures([close, split, moveRight, moveLeft]);
        return menu.Localized();
    }

    private async Task CloseAllDocumentsAsync(EditorPane pane)
    {
        // Snapshot only this pane: removing its last tab activates another pane or creates an empty note.
        foreach (var document in pane.Documents.ToList())
        {
            await CloseDocumentAsync(document);
            if (pane.Documents.Contains(document)) return; // A save prompt was cancelled.
        }
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
