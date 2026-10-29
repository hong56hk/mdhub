using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

internal static class RenameTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new MainWindow(); window.Show();
        void Pump() { using var timeout = new CancellationTokenSource(100); Dispatcher.UIThread.MainLoop(timeout.Token); }
        Pump();
        var view = window.FindControl<MainView>("MainView")!;
        var explorer = window.GetVisualDescendants().OfType<FolderExplorer>().Single();
        explorer.WatchEnabled = false;
        var tree = explorer.FindControl<TreeView>("FolderTree")!;
        var tabs = window.GetVisualDescendants().OfType<ListBox>().Single(c => c.Name == "Tabs");
        TextBox Box() => explorer.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "ExplorerRenameBox");
        void Press(Control target, Key key) { target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key }); Pump(); }
        void Wait(Func<bool> done)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!done() && DateTime.UtcNow < deadline) Pump();
            Check(done(), "operation completes");
        }
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-rename-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var source = Path.Combine(root, "sample.test.md");
            File.WriteAllText(source, "original");
            File.WriteAllText(Path.Combine(root, "existing.md"), "keep");
            var load = explorer.LoadRootAsync(root); Wait(() => load.IsCompleted); load.GetAwaiter().GetResult(); Pump();
            tree.SelectedItem = tree.Items.OfType<TreeViewItem>().Single(i => i.Tag is FolderEntry e && e.Path == source);
            Wait(() => tabs.SelectedItem is NoteDocument { Path: var p } && p == source);
            Pump();
            var document = (NoteDocument)tabs.SelectedItem!;
            document.Editor.Text = "unsaved edits";
            Press(view, Key.F2);
            Check(Box().Text == "sample.test.md" && Box().SelectedText == "sample.test" && window.OwnedWindows.Count == 0,
                "F2 starts inline rename and selects filename without extension");
            Box().Text = "cancelled.md";
            Press(Box(), Key.Escape);
            Check(!explorer.IsRenaming && File.Exists(source), "Escape cancels rename");
            var item = (TreeViewItem)tree.SelectedItem!;
            item.ContextMenu!.Items.OfType<MenuItem>().Single(m => m.Name == "RenameMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Pump();
            Check(explorer.IsRenaming && Box().IsFocused, "context menu starts inline rename");
            Box().Text = "existing.md";
            Press(Box(), Key.Enter);
            Wait(() => !Box().IsReadOnly);
            Check(explorer.IsRenaming && File.ReadAllText(Path.Combine(root, "existing.md")) == "keep" && window.OwnedWindows.Count == 0,
                "duplicate name stays editable and does not overwrite");
            Box().Text = "bad/name.md";
            Press(Box(), Key.Enter);
            Check(explorer.IsRenaming && File.Exists(source), "invalid name remains editable");
            Box().Text = "renamed.md";
            Press(Box(), Key.Enter);
            Wait(() => !explorer.IsRenaming);
            var target = Path.Combine(root, "renamed.md");
            Check(!File.Exists(source) && File.Exists(target) && document.Path == target && document.Text == "unsaved edits" && document.IsDirty,
                "Enter renames and preserves open tab with unsaved edits");
            document.SavedText = document.Text;
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
