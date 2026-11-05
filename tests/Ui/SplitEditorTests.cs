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

internal static class SplitEditorTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        SessionManager.Enabled = false;
        Shortcuts.ResetAll();
        var root = Path.Combine(Path.GetTempPath(), $"mdhub-split-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var note = Path.Combine(root, "note.md");
        var other = Path.Combine(root, "other.txt");
        File.WriteAllText(note, "# Shared\nsecond\nthird");
        File.WriteAllText(other, "one\ntwo\nthree");
        var window = new MainWindow { Width = 1600, Height = 800 };
        void Pump() { using var timeout = new CancellationTokenSource(35); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Wait(Task task) { var until = DateTime.UtcNow.AddSeconds(12); while (!task.IsCompleted && DateTime.UtcNow < until) Pump(); if (!task.IsCompleted) throw new Exception("Operation timed out"); task.GetAwaiter().GetResult(); Pump(); }
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
        void Key(Key key, RawInputModifiers modifiers) { window.KeyPress(key, modifiers, PhysicalKey.None, null); window.KeyRelease(key, modifiers, PhysicalKey.None, null); Pump(); }
        try
        {
            window.Show(); Pump();
            var view = window.GetVisualDescendants().OfType<MainView>().Single();
            EditorPane ActivePane() => (EditorPane)typeof(MainView).GetProperty("ActivePane", Flags)!.GetValue(view)!;
            IReadOnlyList<EditorPane> Panes() => (IReadOnlyList<EditorPane>)typeof(MainView).GetProperty("EditorPanes", Flags)!.GetValue(view)!;
            Task Invoke(string method, params object?[] args) => (Task)typeof(MainView).GetMethod(method, Flags)!.Invoke(view, args)!;
            Wait(view.OpenFolderPathAsync(root));
            Wait(Invoke("OpenPathAsync", note));
            var first = ActivePane();
            var source = first.Active!;
            Key(Avalonia.Input.Key.Left, RawInputModifiers.Alt);
            Key(Avalonia.Input.Key.Right, RawInputModifiers.Alt);
            Check(Panes().Count == 1 && ActivePane() == first, "Pane shortcuts leave a single pane unchanged");
            source.Editor.CaretIndex = 4;
            Key(Avalonia.Input.Key.OemPipe, RawInputModifiers.Control);
            Check(Panes().Count == 2 && ActivePane() != first, "Ctrl+backslash creates and focuses a right pane");
            var second = ActivePane();
            var copy = second.Active!;
            Key(Avalonia.Input.Key.Left, RawInputModifiers.Alt);
            Check(ActivePane() == first && source.Editor.IsKeyboardFocusWithin, "Alt+Left focuses the adjacent left editor");
            Key(Avalonia.Input.Key.Left, RawInputModifiers.Alt);
            Check(ActivePane() == first, "Alt+Left stops at the left edge");
            Key(Avalonia.Input.Key.Right, RawInputModifiers.Alt);
            Check(ActivePane() == second && copy.Editor.IsKeyboardFocusWithin, "Alt+Right focuses the adjacent right editor");
            Key(Avalonia.Input.Key.Right, RawInputModifiers.Alt);
            Check(ActivePane() == second, "Alt+Right stops at the right edge");
            Check(source.Editor.CaretIndex == 4 && copy.Editor.CaretIndex == 4 && source.Text == "# Shared\nsecond\nthird",
                "Switching panes preserves editor positions and content");
            Check(copy.Buffer == source.Buffer && copy.Editor != source.Editor, "Split views share file state and own independent editor controls");
            Check(first.FindControl<TextEditor>("TextEditor")!.IsVisible && second.FindControl<TextEditor>("TextEditor")!.IsVisible
                && !first.FindControl<MdEditor>("MdEditor")!.IsVisible, "Splitting gives both panes room for their source editors");
            source.Editor.SelectionStart = source.Editor.SelectionEnd = 2;
            copy.Editor.SelectionStart = copy.Editor.SelectionEnd = 10;
            copy.Editor.SelectedText = "NEW";
            Pump();
            Check(source.Text == copy.Text && source.IsDirty && copy.IsDirty, "Edits immediately synchronize and mark both views dirty");
            Check(source.Editor.SelectionStart == 2, "Editing another pane preserves an unaffected cursor");
            source.Editor.Focus(); Pump();
            Check(ActivePane() == first, "Keyboard focus changes the active pane");
            var edited = source.Text;
            Key(Avalonia.Input.Key.Z, RawInputModifiers.Control);
            Check(source.Text == "# Shared\nsecond\nthird" && copy.Text == source.Text, "Undo from another pane uses the shared file history");
            Key(Avalonia.Input.Key.Y, RawInputModifiers.Control);
            Check(source.Text == edited && copy.Text == edited, "Redo synchronizes every view");
            Wait(Invoke("SaveAsync", source, false));
            Check(!copy.IsDirty && File.ReadAllText(note) == copy.Text, "Saving any view clears shared dirty state and writes current content");
            Key(Avalonia.Input.Key.P, RawInputModifiers.Control);
            var quick = window.GetVisualDescendants().OfType<QuickOpen>().Single();
            quick.Text = "other:2"; Wait(quick.Pending);
            Key(Avalonia.Input.Key.Enter, RawInputModifiers.Control);
            for (var i = 0; i < 10 && Panes().Count < 3; i++) Pump();
            Check(Panes().Count == 3 && ActivePane().Active?.Path == other, "Quick Open Ctrl+Enter opens the selected file in a new pane");
            Check(ActivePane().Active!.Editor.CaretIndex == 4, "Quick Open preserves the requested line in the new pane");
            var middle = ActivePane();
            Key(Avalonia.Input.Key.Left, RawInputModifiers.Alt);
            Check(ActivePane() == first, "Alt+Left follows pane layout order after inserting a split");
            Key(Avalonia.Input.Key.Right, RawInputModifiers.Alt);
            Key(Avalonia.Input.Key.Right, RawInputModifiers.Alt);
            Check(ActivePane() == second, "Alt+Right navigates through multiple panes");
            Check(Shortcuts.Rebind("view.focusLeftPane", ShortcutGesture.Cmd("PageUp")), "Pane focus shortcuts can be customized");
            Key(Avalonia.Input.Key.PageUp, RawInputModifiers.Control);
            Check(ActivePane() == middle && middle.Active!.Editor.IsKeyboardFocusWithin, "Custom pane shortcut focuses the adjacent editor");
            Shortcuts.ResetAll();
            var explorer = window.GetVisualDescendants().OfType<FolderExplorer>().Single();
            var menu = (ContextMenu)typeof(FolderExplorer).GetMethod("CreateContextMenu", Flags)!.Invoke(explorer, [note, false])!;
            var side = menu.Items.OfType<MenuItem>().Single(m => m.Name == "OpenToSideMenuItem");
            Check(side.Header?.ToString() == "Open to the side", "Explorer file menu exposes Open to the side");
            side.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            for (var i = 0; i < 10 && Panes().Count < 4; i++) Pump();
            Check(Panes().Count == 4 && ActivePane().Active!.Buffer == source.Buffer, "Explorer action opens another synchronized view");
            var folderMenu = (ContextMenu)typeof(FolderExplorer).GetMethod("CreateContextMenu", Flags)!.Invoke(explorer, [root, true])!;
            Check(!folderMenu.Items.OfType<MenuItem>().Any(m => m.Name == "OpenToSideMenuItem"), "Open to the side is restricted to files");
            Key(Avalonia.Input.Key.OemPipe, RawInputModifiers.Control | RawInputModifiers.Shift);
            Check(Panes().Count == 5, "Ctrl+pipe can create the fifth pane");
            var atLimit = ActivePane();
            Wait(Invoke("OpenToSideAsync", other));
            Check(Panes().Count == 5 && ActivePane() == atLimit && ((string?)typeof(MainView).GetProperty("TransientStatus", Flags)!.GetValue(view))?.Contains("5") == true, "The sixth split is refused without changing the active pane");
            var active = atLimit.Active!;
            active.Editor.SelectedText = "edited"; Pump();
            Wait(Invoke("CloseDocumentAsync", active));
            Check(Panes().Count == 4 && source.IsDirty, "Closing a shared dirty view collapses its empty pane without discarding the file");
            var survivor = ActivePane().Active!;
            Check(survivor.Buffer == source.Buffer && survivor.Text.Contains("edited"), "A remaining view retains unsaved changes");
            Wait(Invoke("SaveAsync", survivor, false));
            Wait(Invoke("OpenPathAsync", other));
            Check(ActivePane().Active!.Path == other && second.Active!.Path == note, "Ordinary opening targets the active pane and leaves other panes unchanged");
            var captured = view.CaptureSession();
            Check(captured.Tabs.Count == 2 && captured.Tabs.Contains(note) && captured.Tabs.Contains(other), "Session capture includes files from every pane without duplicate paths");
            typeof(MainView).GetMethod("UpdateMovedDocuments", Flags)!.Invoke(view, [note, Path.Combine(root, "renamed.md")]);
            Check(source.Path == Path.Combine(root, "renamed.md") && copy.Path == source.Path, "Rename updates the shared file identity across panes");
            Check(Panes().All(p => p.Bounds.Width > 0 && p.FindControl<TextEditor>("TextEditor")!.Bounds.Width > 0), "Every remaining pane has usable layout bounds");
            var imagePath = Path.Combine(root, "picture.png");
            using (var fixture = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(8, 8)))
                fixture.Save(imagePath, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            Wait(Invoke("OpenPathAsync", imagePath));
            var image = ActivePane().Active!;
            Wait(Invoke("OpenToSideAsync", imagePath));
            var imageCopy = ActivePane().Active!;
            Check(imageCopy.Image == image.Image && imageCopy.Buffer == image.Buffer, "Image views share the loaded resource");
            Wait(Invoke("CloseDocumentAsync", imageCopy));
            using (var stream = new MemoryStream()) image.Image!.Save(stream, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            Check(Panes().Count == 4, "Closing one image view leaves the other image resource usable");
            var beforeFailure = ActivePane();
            try { Wait(Invoke("OpenToSideAsync", Path.Combine(root, "missing.txt"))); throw new Exception("Missing file unexpectedly opened"); }
            catch (FileNotFoundException) { }
            Check(Panes().Count == 4 && ActivePane() == beforeFailure, "A failed side-open removes its empty pane and restores the original pane");
            var longPath = Path.Combine(root, "long.txt");
            File.WriteAllText(longPath, string.Join("\n", Enumerable.Range(1, 300).Select(n => $"Line {n}")));
            Wait(Invoke("OpenPathAsync", longPath));
            var longSource = ActivePane().Active!;
            Wait(Invoke("OpenToSideAsync", longPath));
            var longCopy = ActivePane().Active!;
            var leftScroll = longSource.Editor.GetVisualDescendants().OfType<ScrollViewer>().First();
            var rightScroll = longCopy.Editor.GetVisualDescendants().OfType<ScrollViewer>().First();
            leftScroll.Offset = new Vector(0, 200);
            rightScroll.Offset = new Vector(0, 500);
            Pump();
            var offset = leftScroll.Offset;
            longCopy.Editor.CaretIndex = longCopy.Text.Length;
            longCopy.Editor.SelectedText = "\nAdded";
            Pump(); Pump();
            Check(leftScroll.Offset == offset && rightScroll.Offset != leftScroll.Offset, "Edits preserve another pane's independent scroll position");
            Wait(Invoke("SaveAsync", longCopy, false));
            using (var frame = window.CaptureRenderedFrame())
                frame?.Save(Path.Combine(root, "preview.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            Console.WriteLine("Split editor tests passed.");
        }
        finally { var main = window.GetVisualDescendants().OfType<MainView>().FirstOrDefault(); if (main is not null) typeof(MainView).GetField("_allowClose", Flags)!.SetValue(main, true); window.GetVisualDescendants().OfType<MainView>().FirstOrDefault()?.StopPreview(); window.Close(); Directory.Delete(root, true); }
    }
}
