using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Services;

// Item 53: explorer file shortcuts (Ctrl+X/C/V/D, Del), copy naming, paste targets, menu hints and focus scoping.
internal static class ExplorerShortcutTests
{
    public static void Run()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine("r", "note-copy.md") };
        Check(ExplorerFileOperations.UniqueCopyPath(Path.Combine("r", "note.md"), false, _ => false) == Path.Combine("r", "note-copy.md"), "first copy is note-copy.md");
        Check(ExplorerFileOperations.UniqueCopyPath(Path.Combine("r", "note.md"), false, taken.Contains) == Path.Combine("r", "note-copy-2.md"), "then note-copy-2.md");
        taken.Add(Path.Combine("r", "note-copy-2.md"));
        Check(ExplorerFileOperations.UniqueCopyPath(Path.Combine("r", "note.md"), false, taken.Contains) == Path.Combine("r", "note-copy-3.md"), "then note-copy-3.md");
        Check(ExplorerFileOperations.UniqueCopyPath(Path.Combine("r", "Docs"), true, _ => false) == Path.Combine("r", "Docs-copy"), "folders get -copy too");
        Check(ExplorerFileOperations.UniqueCopyPath(Path.Combine("r", "a.tar.gz"), false, _ => false) == Path.Combine("r", "a.tar-copy.gz"), "suffix goes before the last extension");
        var root = Path.Combine("C:", "r");
        Check(FolderExplorer.PasteTarget(root, Path.Combine(root, "sub", "f.md"), false) == Path.Combine(root, "sub"), "paste on a file goes into its folder");
        Check(FolderExplorer.PasteTarget(root, Path.Combine(root, "sub"), true) == Path.Combine(root, "sub"), "paste on a folder goes inside it");
        Check(FolderExplorer.PasteTarget(root, null, false) == root, "paste with nothing selected goes into the root");
        Check(!Shortcuts.Conflicts().Any(), "explorer shortcuts do not conflict with the editor's Ctrl+C/X/V");
        Check(Shortcuts.Format("explorer.cut", false) == "Ctrl+X" && Shortcuts.Format("explorer.copy", false) == "Ctrl+C" && Shortcuts.Format("explorer.paste", false) == "Ctrl+V"
            && Shortcuts.Format("explorer.duplicate", false) == "Ctrl+D" && Shortcuts.Format("explorer.rename", false) == "F2" && Shortcuts.Format("explorer.delete", false) == "Del", "default explorer gestures");
        foreach (var language in Loc.Languages.Select(l => l.Code))
        {
            Loc.Set(language);
            foreach (var key in new[] { "Sc.explorer.cut", "Sc.explorer.copy", "Sc.explorer.paste", "Sc.explorer.duplicate", "Sc.explorer.delete", "MoveToRecycleBin" })
                Check(Loc.T(key) != key && !string.IsNullOrWhiteSpace(Loc.T(key)), $"{key} is localized in {language}");
        }
        Loc.Set(Loc.Languages[0].Code);
        Check(Loc.T("MoveToRecycleBin") == "Move the file to Recycle Bin", "delete confirmation button text");

        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        void Pump() { using var timeout = new CancellationTokenSource(30); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Await(Task task) { var end = DateTime.UtcNow.AddSeconds(10); while (!task.IsCompleted && DateTime.UtcNow < end) Pump(); task.GetAwaiter().GetResult(); }
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-explorer-keys-" + Guid.NewGuid().ToString("N"))).FullName;
        var explorer = new FolderExplorer { WatchEnabled = false };
        var editor = new TextBox { Text = "editor text" };
        var window = new Window { Width = 700, Height = 500, Content = new DockPanel { Children = { explorer, editor } } };
        try
        {
            var sub = Directory.CreateDirectory(Path.Combine(folder, "sub")).FullName;
            var file = Path.Combine(sub, "note.md"); File.WriteAllText(file, "x");
            window.Show(); Pump();
            var requests = new List<ExplorerRequest>();
            explorer.ExplorerRequested += requests.Add;
            explorer.CanPaste = () => true;
            Await(explorer.LoadRootAsync(folder));
            Await(explorer.RevealAsync(file, focus: true)); Pump();
            var tree = explorer.GetVisualDescendants().OfType<TreeView>().Single(t => t.Name == "FolderTree");
            void Press(Control target, Key key, KeyModifiers mods) { target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods, Source = target }); Pump(); }
            var ctrl = ShortcutKeys.Modifiers(ShortcutMods.Command);
            Press(tree, Key.D, ctrl); Press(tree, Key.C, ctrl); Press(tree, Key.X, ctrl); Press(tree, Key.V, ctrl); Press(tree, Key.Delete, KeyModifiers.None);
            Check(requests.Select(r => r.Action).SequenceEqual([ExplorerAction.Duplicate, ExplorerAction.Copy, ExplorerAction.Cut, ExplorerAction.Paste, ExplorerAction.Delete]),
                $"explorer keys raise duplicate, copy, cut, paste and delete ({string.Join(",", requests.Select(r => r.Action))})");
            Check(requests.Where(r => r.Action != ExplorerAction.Paste).All(r => r.Path == file) && requests.Single(r => r.Action == ExplorerAction.Paste).Path == sub, "actions target the selection; paste on a file goes into its folder");
            requests.Clear();
            Await(explorer.RevealAsync(sub, focus: true)); Pump();
            Press(tree, Key.V, ctrl);
            Check(requests.Single().Path == sub, "paste on a selected folder goes inside it");
            requests.Clear();
            editor.Focus();
            foreach (var key in new[] { Key.C, Key.V, Key.X, Key.D }) Press(editor, key, ctrl);
            Press(editor, Key.Delete, KeyModifiers.None);
            Check(requests.Count == 0, "editor keys never reach the explorer shortcuts");
            Check(Shortcuts.Rebind("explorer.duplicate", new ShortcutGesture("K", ShortcutMods.Command | ShortcutMods.Alt)), "duplicate can be rebound");
            Press(tree, Key.D, ctrl);
            Check(requests.Count == 0, "the old gesture stops working after a rebind");
            Press(tree, Key.K, ctrl | KeyModifiers.Alt);
            Check(requests.Single().Action == ExplorerAction.Duplicate, "the new gesture duplicates");
            var row = tree.GetVisualDescendants().OfType<TreeViewItem>().First(i => i.Tag is FolderEntry e && e.Path == sub);
            var menu = row.ContextMenu!;
            var duplicate = menu.Items.OfType<MenuItem>().Single(i => i.Name == "DuplicateMenuItem");
            Check(ShortcutKeys.MenuText(duplicate) == "Ctrl+Alt+K", $"menu hint follows the rebind ({ShortcutKeys.MenuText(duplicate)})");
            Shortcuts.ResetAll();
            var hints = menu.Items.OfType<MenuItem>().Where(i => ShortcutKeys.GetId(i) is not null).ToDictionary(i => i.Name!, ShortcutKeys.MenuText);
            Check(hints["CutMenuItem"] == "Ctrl+X" && hints["CopyMenuItem"] == "Ctrl+C" && hints["DuplicateMenuItem"] == "Ctrl+D"
                && hints["RenameMenuItem"] == "F2" && hints["DeleteMenuItem"] == "Del", $"context menu shows shortcut hints ({string.Join(",", hints)})");
            var paste = tree.ContextMenu!.Items.OfType<MenuItem>().Single(i => i.Name == "BackgroundPasteMenuItem");
            Check(ShortcutKeys.MenuText(paste) == "Ctrl+V", "the explorer Paste menu item shows Ctrl+V");
        }
        finally { Shortcuts.ResetAll(); window.Close(); Directory.Delete(folder, true); }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}