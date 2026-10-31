using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Bug 85: real Ctrl+C/X/V key presses (through the window) act on explorer files when the explorer has focus,
// exactly like the context menu's Copy/Cut/Paste, and on text when the editor has focus.
internal static class ExplorerClipboardKeyTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new MainWindow { Width = 1100, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var view = window.FindControl<MainView>("MainView")!;
        var explorer = window.GetVisualDescendants().OfType<FolderExplorer>().Single();
        explorer.WatchEnabled = false;
        var tree = explorer.FindControl<TreeView>("FolderTree")!;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        void Pump() { for (var k = 0; k < 3; k++) { using var timeout = new CancellationTokenSource(20); Dispatcher.UIThread.MainLoop(timeout.Token); } }
        bool Busy() => (bool)typeof(MainView).GetField("_busy", flags)!.GetValue(view)!;
        void WaitFor(Func<bool> condition) { var end = DateTime.UtcNow.AddSeconds(10); while ((!condition() || Busy()) && DateTime.UtcNow < end) Pump(); Pump(); }
        void Run(string method, params object[] args) { var t = (Task)typeof(MainView).GetMethod(method, flags)!.Invoke(view, args)!; WaitFor(() => t.IsCompleted); t.GetAwaiter().GetResult(); }
        var ctrl = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
        void Key(Key k, RawInputModifiers m = RawInputModifiers.None) { var mk = OperatingSystem.IsMacOS() ? Avalonia.Input.Key.LWin : Avalonia.Input.Key.LeftCtrl; if (m != RawInputModifiers.None) { window.KeyPress(mk, m, PhysicalKey.ControlLeft, null); Pump(); } window.KeyPress(k, m, PhysicalKey.None, k.ToString().ToLowerInvariant()); window.KeyRelease(k, m, PhysicalKey.None, null); if (m != RawInputModifiers.None) window.KeyRelease(mk, RawInputModifiers.None, PhysicalKey.ControlLeft, null); Pump(); }
        string? ClipText() => window.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult();
        IReadOnlyList<string>? Published() => (IReadOnlyList<string>?)typeof(MainView).GetProperty("PublishedClipboardFiles", flags)!.GetValue(view);
        (string Path, bool IsCut)? Clip() => ((string, bool)?)typeof(MainView).GetProperty("ExplorerClipboard", flags)!.GetValue(view);
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-clipkeys-" + Guid.NewGuid().ToString("N"))).FullName;
        string P(params string[] parts) => Path.Combine([root, .. parts]);
        try
        {
            Directory.CreateDirectory(P("Dest"));
            File.WriteAllText(P("note.md"), "note body text");
            File.WriteAllText(P("other.md"), "other");
            Run("LoadRootAsync", root);
            TreeViewItem Row(string path) { var stack = new Stack<TreeViewItem>(tree.Items.OfType<TreeViewItem>()); while (stack.Count > 0) { var i = stack.Pop(); if (i.Tag is FolderEntry e && string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)) return i; foreach (var c in i.Items.OfType<TreeViewItem>()) stack.Push(c); } throw new Exception("no row " + path); }
            Point At(TreeViewItem i) { window.UpdateLayout(); var h = i.GetVisualDescendants().OfType<Control>().First(c => c.Name == "PART_HeaderPresenter"); return h.TranslatePoint(new Point(10, h.Bounds.Height / 2), window)!.Value; }
            void Click(string path, RawInputModifiers m = RawInputModifiers.None) { WaitFor(() => true); var p = At(Row(path)); // Hover tooltips are overlay popups in headless mode and would swallow the click.
                foreach (var c in window.GetVisualDescendants().OfType<Control>()) if (ToolTip.GetIsOpen(c)) ToolTip.SetIsOpen(c, false); Pump(); window.MouseDown(p, MouseButton.Left, m); window.MouseUp(p, MouseButton.Left, m); WaitFor(() => true); }

            // Explorer focus: Ctrl+C copies the file (not its text), Ctrl+V pastes it with the -copy suffix.
            window.Clipboard!.SetTextAsync("sentinel").GetAwaiter().GetResult();
            Click(P("note.md"));
            Check(tree.IsKeyboardFocusWithin, "a click leaves focus in the explorer");
            Key(Avalonia.Input.Key.C, ctrl); WaitFor(() => Clip() is not null);
            Check(Clip() is { IsCut: false } c1 && c1.Path == P("note.md"), $"explorer Ctrl+C copies the file ({Clip()})");
            Check(ClipText() is not ("sentinel" or "note body text"), $"explorer Ctrl+C replaces the clipboard text, never with the file's text ({ClipText()})");
            Check(Published() is [var pub] && pub == P("note.md"), "explorer Ctrl+C puts the file itself on the system clipboard");
            var clipFiles = window.Clipboard!.TryGetFilesAsync().GetAwaiter().GetResult(); Check(clipFiles is [var cf] && cf.TryGetLocalPath() == P("note.md"), "the system clipboard holds the file, so File Explorer can paste it");
            explorer.FocusTree(); Pump();
            Key(Avalonia.Input.Key.V, ctrl); WaitFor(() => File.Exists(P("note-copy.md")));
            Check(File.Exists(P("note-copy.md")) && File.ReadAllText(P("note-copy.md")) == "note body text", "explorer Ctrl+V pastes the file with the -copy suffix");
            Check(File.ReadAllText(P("note.md")) == "note body text", "explorer Ctrl+V does not paste text into the note");

            // Ctrl+X then Ctrl+V on a folder moves the file into it.
            // The paste left focus in the editor; a single click on a file row must take focus back to the explorer.
            Check(!tree.IsKeyboardFocusWithin, "an explorer paste moves focus to the editor");
            Click(P("other.md"));
            Check(tree.IsKeyboardFocusWithin, $"a single click on a file from the editor focuses the explorer");
            Key(Avalonia.Input.Key.X, ctrl); WaitFor(() => Clip() is { IsCut: true });
            Check(Clip() is { IsCut: true } c2 && c2.Path == P("other.md"), "explorer Ctrl+X cuts the file");
            Click(P("Dest"));
            Key(Avalonia.Input.Key.V, ctrl); WaitFor(() => File.Exists(P("Dest", "other.md")));
            Check(File.Exists(P("Dest", "other.md")) && !File.Exists(P("other.md")), "explorer Ctrl+V after Ctrl+X moves the file");

            // Item 80: the same keys act on a multi-selection.
            Click(P("note.md")); Click(P("note-copy.md"), ctrl);
            Check(explorer.IsMultiSelect, "two files selected");
            Key(Avalonia.Input.Key.C, ctrl); WaitFor(() => typeof(MainView).GetProperty("ExplorerClipboardPaths", flags)!.GetValue(view) is IReadOnlyList<string> { Count: 2 });
            Click(P("Dest"));
            Key(Avalonia.Input.Key.V, ctrl); WaitFor(() => File.Exists(P("Dest", "note-copy.md")));
            Check(File.Exists(P("Dest", "note.md")) && File.Exists(P("Dest", "note-copy.md")) && Published() is { Count: 2 }, "explorer Ctrl+C/V copy and paste a multi-selection (both files on the system clipboard)");

            // Editor focus: Ctrl+C copies text and leaves the explorer clipboard alone.
            Click(P("note.md"));
            var before = Clip();
            var box = view.FindControl<Mdhub.Components.TextEditor>("TextEditor")!.GetVisualDescendants().OfType<TextBox>().First(b => b.IsEffectivelyVisible && b.AcceptsReturn);
            box.Focus(); Pump();
            box.SelectionStart = 0; box.SelectionEnd = 4; Pump();
            Key(Avalonia.Input.Key.C, ctrl); WaitFor(() => ClipText() == "note");
            Check(ClipText() == "note" && Equals(Clip(), before), $"editor Ctrl+C copies the selected text ({ClipText()})");
            box.SelectionStart = box.SelectionEnd = box.Text!.Length; Pump();
            var files = Directory.GetFiles(root).Length;
            Key(Avalonia.Input.Key.V, ctrl); WaitFor(() => box.Text!.EndsWith("note"));
            Check(box.Text!.EndsWith("textnote") && Directory.GetFiles(root).Length == files, $"editor Ctrl+V pastes text, not files ({box.Text})");
        }
        finally { window.Close(); try { Directory.Delete(root, true); } catch { } }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
