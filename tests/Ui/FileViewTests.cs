using Avalonia.Input.Platform;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Views;

internal static class FileViewTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var view = window.FindControl<MainView>("MainView")!;
        T Find<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
        var tabs = Find<ListBox>("Tabs");
        var textEditor = Find<TextEditor>("TextEditor");
        var preview = Find<MdEditor>("MdEditor");
        var imageViewer = Find<ImageViewer>("ImageViewer");
        void Invoke(string name, params object[] arguments)
        {
            var task = (Task)typeof(MainView).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, arguments)!;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
            {
                using var timeout = new CancellationTokenSource(20);
                Dispatcher.UIThread.MainLoop(timeout.Token);
            }
            Check(task.IsCompleted, "operation completes");
            task.GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
        }
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-views-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var markdown = Path.Combine(root, "note.md");
            File.WriteAllText(markdown, "# Markdown");
            Invoke("OpenPathAsync", markdown);
            var mdDocument = (NoteDocument)tabs.SelectedItem!;
            Check(textEditor.IsVisible && preview.IsVisible && !imageViewer.IsVisible, "Markdown retains both panes");
            var toggle = Find<Button>("TogglePreviewButton");
            toggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Check(!preview.IsVisible && Grid.GetColumnSpan(textEditor) == 3, "toolbar toggle hides preview and expands editor");
            view.RaiseEvent(new Avalonia.Input.KeyEventArgs
            {
                RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.OemPipe,
                KeyModifiers = Avalonia.Input.KeyModifiers.Control
            });
            Check(preview.IsVisible && Grid.GetColumnSpan(textEditor) == 1, "Ctrl+backslash restores preview");
            foreach (var extension in new[] { ".txt", ".cs", ".json", ".csv", ".py", "" })
            {
                var path = Path.Combine(root, "text" + extension);
                File.WriteAllText(path, "plain content");
                Invoke("OpenPathAsync", path);
                var document = (NoteDocument)tabs.SelectedItem!;
                Check(textEditor.IsVisible && Grid.GetColumnSpan(textEditor) == 3 && !preview.IsVisible && !imageViewer.IsVisible,
                    "full-width text view for " + extension);
                Check(!Find<StackPanel>("FormatBar").IsEnabled, "format buttons disabled");
                document.Editor.Text = "edited content";
                Invoke("SaveAsync", document, false);
                Check(File.ReadAllText(path) == "edited content", "text saves to original file");
            }
            var png = Path.Combine(root, "image.png");
            using (var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(20, 10))) bitmap.Save(png);
            var originalBytes = File.ReadAllBytes(png);
            Invoke("OpenPathAsync", png);
            var imageDocument = (NoteDocument)tabs.SelectedItem!;
            Check(imageDocument.IsImage && imageViewer.IsVisible && !textEditor.IsVisible && !preview.IsVisible,
                "image fills content area instead of editors");
            Check(Find<Image>("Picture").Source is not null && !Find<StackPanel>("FormatBar").IsEnabled, "image rendered and formatting disabled");
            Invoke("SaveAsync", imageDocument, false);
            Check(File.ReadAllBytes(png).SequenceEqual(originalBytes), "Save cannot corrupt image data");
            tabs.SelectedItem = mdDocument;
            Dispatcher.UIThread.RunJobs();
            Check(preview.IsVisible && textEditor.IsVisible && Grid.GetColumnSpan(textEditor) == 1 && Find<StackPanel>("FormatBar").IsEnabled,
                "returning to Markdown restores split layout and formatting");
            tabs.SelectedItem = imageDocument;
            Check(imageViewer.IsVisible && Find<Image>("Picture").Source is not null, "image tab can be revisited");
            var binary = Path.Combine(root, "binary.bin");
            File.WriteAllBytes(binary, [0, 1, 2, 3]);
            Invoke("OpenPathAsync", binary);
            Check(ReferenceEquals(tabs.SelectedItem, imageDocument), "binary rejection keeps active tab");
            var invalid = Path.Combine(root, "invalid.dat");
            File.WriteAllBytes(invalid, [0x92, 0x81, 0xff]);
            var utf16 = Path.Combine(root, "unicode.txt");
            File.WriteAllText(utf16, "Unicode text", System.Text.Encoding.Unicode);
            // Bug 39: legacy and BOM-less notes are never dimmed, open with their encoding and save back unchanged.
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            Mdhub.Services.TextFileStore.AnsiCodePageOverride = 950;
            var legacyText = "20170426 \u8003\u8eca\r\n\u4eca\u5929\u5f88\u4e0d\u958b\u5fc3\r\n";
            var notes = new (string Name, byte[] Bytes, string Text)[]
            {
                ("20170226_unhappy.txt", System.Text.Encoding.GetEncoding(950).GetBytes(legacyText), legacyText),
                ("20170426_\u8003\u8eca.txt", new System.Text.UnicodeEncoding(false, false).GetBytes(legacyText), legacyText),
                ("bom.md", [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes(legacyText)], legacyText),
                ("control.txt", System.Text.Encoding.UTF8.GetBytes("a\0b\u0001c"), "a\0b\u0001c"),
            };
            foreach (var note in notes) File.WriteAllBytes(Path.Combine(root, note.Name), note.Bytes);
            var video = Path.Combine(root, "clip.mp4"); File.WriteAllBytes(video, [0, 0, 0, 0x18]);
            Invoke("LoadRootAsync", root);
            var tree = Find<TreeView>("FolderTree");
            foreach (var note in notes)
            {
                var path = Path.Combine(root, note.Name);
                var item = tree.Items.OfType<TreeViewItem>().Single(i => i.Tag is Mdhub.Services.FolderEntry e && e.Path == path);
                Check(item.Opacity == 1, $"{note.Name} is not dimmed in the explorer");
                Invoke("OpenPathAsync", path);
                var opened = (NoteDocument)tabs.SelectedItem!;
                Check(opened.Path == path && opened.Editor.Text == note.Text, $"{note.Name} opens with its text ({opened.Encoding.WebName})");
                Invoke("SaveAsync", opened, false);
                Check(File.ReadAllBytes(path).SequenceEqual(note.Bytes), $"{note.Name} saves back in its own encoding");
            }
            Mdhub.Services.TextFileStore.AnsiCodePageOverride = null;
            Check(tree.Items.OfType<TreeViewItem>().Single(i => i.Tag is Mdhub.Services.FolderEntry e && e.Path == video).Opacity < 1, ".mp4 stays dimmed");
            tabs.SelectedItem = imageDocument; Dispatcher.UIThread.RunJobs();
            foreach (var path in new[] { binary, invalid })
            {
                var item = tree.Items.OfType<TreeViewItem>().Single(i => i.Tag is Mdhub.Services.FolderEntry e && e.Path == path);
                Check(item.Opacity < 1, "unsupported explorer item is dimmed");
                tree.SelectedItem = item;
                Dispatcher.UIThread.RunJobs();
                Check(ReferenceEquals(tabs.SelectedItem, imageDocument) && window.OwnedWindows.Count == 0, "unsupported selection does not open a tab or error dialog");
            }
            Check(Mdhub.Services.FileSupport.Detect(utf16) == Mdhub.Services.SupportedFileKind.Text, "BOM-encoded Unicode text remains supported");

            // Item 40: clicking the status-bar path copies it and briefly confirms; untitled notes do nothing.
            void Wait(Task task)
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!task.IsCompleted && DateTime.UtcNow < deadline) { using var t = new CancellationTokenSource(20); Dispatcher.UIThread.MainLoop(t.Token); }
                Check(task.IsCompleted, "clipboard operation completes");
            }
            var statusBar = window.GetVisualDescendants().OfType<BottomStatusBar>().Single();
            var statusText = statusBar.FindControl<TextBlock>("Status")!;
            var clipboard = TopLevel.GetTopLevel(window)!.Clipboard!;
            BottomStatusBar.ConfirmationDuration = TimeSpan.FromMilliseconds(150);
            Invoke("OpenPathAsync", markdown);
            mdDocument.Editor.Text += " edited"; Dispatcher.UIThread.RunJobs();
            Check(statusBar.CopyablePath == markdown && statusText.Text!.EndsWith(markdown), "the status bar shows the full path of a saved note");
            Check(statusText.Classes.Contains("pathLink") && statusText.Cursor?.ToString() == new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand).ToString()
                && ToolTip.GetTip(statusText) is string { Length: > 0 }, "the path has a hand cursor and a tooltip");
            Check(statusBar.Styles.OfType<Avalonia.Styling.Style>().Any(s => s.Setters.OfType<Avalonia.Styling.Setter>().Any(x => x.Property == TextBlock.TextDecorationsProperty)),
                "hovering the path underlines it");
            Wait(clipboard.SetTextAsync("old"));
            var shown = statusText.Text;
            statusText.RaiseEvent(new Avalonia.Input.PointerReleasedEventArgs(statusText, new Avalonia.Input.Pointer(1, Avalonia.Input.PointerType.Mouse, true), window, new Point(2, 2), 0,
                new Avalonia.Input.PointerPointProperties(Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PointerUpdateKind.LeftButtonReleased), Avalonia.Input.KeyModifiers.None, Avalonia.Input.MouseButton.Left));
            for (var i = 0; i < 5; i++) { using var t = new CancellationTokenSource(20); Dispatcher.UIThread.MainLoop(t.Token); }
            var copied = clipboard.TryGetTextAsync(); Wait(copied);
            Check(copied.Result == markdown, "clicking the path copies the full path (without the unsaved prefix)");
            Check(statusText.Text == Mdhub.Localization.Loc.T("PathCopied") && statusText.Text == "Path copied", "a brief Path copied confirmation replaces the path");
            var restoreBy = DateTime.UtcNow.AddSeconds(3);
            while (statusText.Text != shown && DateTime.UtcNow < restoreBy) { using var t = new CancellationTokenSource(20); Dispatcher.UIThread.MainLoop(t.Token); }
            Check(statusText.Text == shown, "the path comes back after the confirmation");
            foreach (var code in new[] { "zh-Hant", "ja", "ko" })
                Check(Mdhub.Localization.Loc.Table(code) is { } table && table["PathCopied"] is { Length: > 0 } c && c != "Path copied" && table["CopyPathTip"] is { Length: > 0 }, $"Path copied is localized ({code})");
            var untitled = (NoteDocument)typeof(MainView).GetMethod("AddDocument", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, [null, "scratch", null, null])!;
            Dispatcher.UIThread.RunJobs();
            Check(ReferenceEquals(tabs.SelectedItem, untitled) && statusBar.CopyablePath is null && !statusText.Classes.Contains("pathLink") && ToolTip.GetTip(statusText) is null, "an untitled note has no clickable path");
            Wait(clipboard.SetTextAsync("old")); var before = statusText.Text;
            var noop = statusBar.CopyPathAsync(); Wait(noop);
            var after = clipboard.TryGetTextAsync(); Wait(after);
            Check(!noop.Result && after.Result == "old" && statusText.Text == before, "clicking an untitled note path does nothing");
        }
        finally { window.Close(); Directory.Delete(root, recursive: true); }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
