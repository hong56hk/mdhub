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
            Invoke("LoadRootAsync", root);
            var tree = Find<TreeView>("FolderTree");
            foreach (var path in new[] { binary, invalid })
            {
                var item = tree.Items.OfType<TreeViewItem>().Single(i => i.Tag is Mdhub.Services.FolderEntry e && e.Path == path);
                Check(item.Opacity < 1, "unsupported explorer item is dimmed");
                tree.SelectedItem = item;
                Dispatcher.UIThread.RunJobs();
                Check(ReferenceEquals(tabs.SelectedItem, imageDocument) && window.OwnedWindows.Count == 0, "unsupported selection does not open a tab or error dialog");
            }
            Check(Mdhub.Services.FileSupport.Detect(utf16) == Mdhub.Services.SupportedFileKind.Text, "BOM-encoded Unicode text remains supported");
        }
        finally { window.Close(); Directory.Delete(root, recursive: true); }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
