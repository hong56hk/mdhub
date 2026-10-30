using Avalonia.Controls;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;

namespace Mdhub.Views;

// Item 41: Reveal in Side Bar (editor and preview context menus, Ctrl+E, and Ctrl+Shift+E). Distinct from the
// explorer's "Reveal in file explorer", which opens the system file manager.
public partial class MainView
{
    internal bool CanRevealInSidebar => _active?.Path is { } path && FolderExplorer.Contains(path);

    // Shows the explorer, selects the active file (expanding its parent folders), scrolls it into view and focuses it.
    // Untitled notes and files outside the open folder only get a status note.
    internal async Task<bool> RevealInSidebarAsync(bool quietWhenUnavailable = false)
    {
        if (_busy || FolderExplorer.IsRenaming) return false;
        if (_active?.Path is not { } path) { if (!quietWhenUnavailable) ShowTransientStatus(Loc.T("RevealNoPath")); return false; }
        if (!FolderExplorer.Contains(path)) { if (!quietWhenUnavailable) ShowTransientStatus(Loc.T("RevealOutsideFolder")); return false; }
        ShowSidebar(false);
        return await FolderExplorer.RevealAsync(path, quiet: true, focus: true);
    }

    // The right-click menu of the source editor and the preview.
    private ContextMenu CreateEditorMenu(NoteDocument? document)
    {
        MenuItem Item(string name, string key, Action click, string? shortcut = null)
        {
            var item = LocResources.Item(key);
            item.Name = name;
            if (shortcut is not null) ShortcutKeys.SetId(item, shortcut);
            item.Click += (_, _) => click();
            return item;
        }
        Separator Line() => new() { Background = ThemeService.Brush("SeparatorForeground"), Foreground = ThemeService.Brush("SeparatorForeground") };
        var reveal = Item("RevealInSidebarMenuItem", "RevealInSidebar", () => _ = RevealInSidebarAsync(), "file.revealInSidebar");
        var system = Item("RevealInSystemMenuItem", "Reveal", () => { if (_active?.Path is { } p) ExplorerFileOperations.Reveal(p); });
        var menu = new ContextMenu();
        if (document is not null)
        {
            var editor = document.Editor;
            var cut = Item("EditorCutMenuItem", "MenuCut", () => editor.Cut());
            var copy = Item("EditorCopyMenuItem", "MenuCopy", () => editor.Copy());
            var paste = Item("EditorPasteMenuItem", "MenuPaste", () => _ = PasteAsync());
            var all = Item("EditorSelectAllMenuItem", "MenuSelectAll", () => editor.SelectAll());
            foreach (var item in new Control[] { cut, copy, paste, all, Line() }) menu.Items.Add(item);
            void Edit() { cut.IsEnabled = editor.CanCut; copy.IsEnabled = editor.CanCopy; paste.IsEnabled = !editor.IsReadOnly; }
            menu.Opening += (_, _) => Edit();
            menu.Opened += (_, _) => Edit();
        }
        menu.Items.Add(reveal);
        menu.Items.Add(system);
        // Opening covers a right-click; Opened also covers a menu opened from code or the keyboard.
        void Update() { reveal.IsEnabled = !_busy && CanRevealInSidebar; system.IsEnabled = _active?.Path is not null; }
        menu.Opening += (_, _) => Update();
        menu.Opened += (_, _) => Update();
        ShortcutKeys.ApplyGestures([reveal]);
        return menu.Localized();
    }
}