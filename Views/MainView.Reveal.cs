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
            // Item 45: Undo and Redo come first, with their gestures and live enabled state.
            var undo = Item("EditorUndoMenuItem", "MenuUndo", () => editor.Undo(), "edit.undo");
            var redo = Item("EditorRedoMenuItem", "MenuRedo", () => editor.Redo(), "edit.redo");
            var cut = Item("EditorCutMenuItem", "MenuCut", () => editor.Cut());
            var copy = Item("EditorCopyMenuItem", "MenuCopy", () => editor.Copy());
            var paste = Item("EditorPasteMenuItem", "MenuPaste", () => _ = PasteAsync());
            var all = Item("EditorSelectAllMenuItem", "MenuSelectAll", () => editor.SelectAll());
            foreach (var item in new Control[] { undo, redo, Line(), cut, copy, paste, all, Line() }) menu.Items.Add(item);
            void Edit() { undo.IsEnabled = editor.CanUndo && !editor.IsReadOnly; redo.IsEnabled = editor.CanRedo && !editor.IsReadOnly; cut.IsEnabled = editor.CanCut; copy.IsEnabled = editor.CanCopy; paste.IsEnabled = !editor.IsReadOnly; }
            menu.Opening += (_, _) => Edit();
            menu.Opened += (_, _) => Edit();
            // Item 74: a misspelled word under the click (or the caret) puts up to 5 suggestions at the top, then
            // Add to dictionary and Ignore in this file. Rebuilt on every open; Localized() pads and relabels them.
            var spelling = new List<Control>();
            menu.Opening += (_, _) =>
            {
                foreach (var c in spelling) menu.Items.Remove(c);
                spelling.Clear();
                var index = (editor as MarkdownTextBox)?.ContextIndex ?? editor.CaretIndex;
                if (_active != document || editor.IsReadOnly || TextEditor.Spelling.MisspellingAt(index) is not { } word) return;
                foreach (var suggestion in SpellCheck.Suggest(word.Word))
                {
                    var fix = new MenuItem { Name = "SpellSuggestionMenuItem", Header = suggestion, FontWeight = Avalonia.Media.FontWeight.SemiBold };
                    fix.Click += (_, _) => TextEditor.Spelling.Replace(word, suggestion);
                    spelling.Add(fix);
                }
                if (spelling.Count == 0) { var none = LocResources.Item("SpellNoSuggestions"); none.Name = "SpellNoSuggestionsMenuItem"; none.IsEnabled = false; spelling.Add(none); }
                spelling.Add(Item("SpellAddMenuItem", "SpellAddToDictionary", () => SpellCheck.AddWord(word.Word)));
                spelling.Add(Item("SpellIgnoreMenuItem", "SpellIgnoreInFile", () => SpellCheck.Ignore(editor, word.Word)));
                spelling.Add(Line());
                for (var i = 0; i < spelling.Count; i++) menu.Items.Insert(i, spelling[i]);
            };
        }
        menu.Items.Add(reveal);
        menu.Items.Add(system);
        // Opening covers a right-click; Opened also covers a menu opened from code or the keyboard.
        void Update() { reveal.IsEnabled = !_busy && CanRevealInSidebar; system.IsEnabled = _active?.Path is not null; }
        menu.Opening += (_, _) => Update();
        menu.Opened += (_, _) => Update();
        ShortcutKeys.ApplyGestures(menu.Items.OfType<MenuItem>().Where(i => ShortcutKeys.GetId(i) is not null).ToList());
        return menu.Localized();
    }
}