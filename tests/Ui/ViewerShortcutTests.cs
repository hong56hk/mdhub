using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Models;
using Mdhub.Views;

// Bug 86: Ctrl+W and the other global tab shortcuts work whatever has (or lost) focus, e.g. a PDF's binary placeholder
// opened from the editor, where the hidden editor drops keyboard focus and keys reach only the window.
internal static class ViewerShortcutTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new MainWindow { Width = 1000, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var view = window.FindControl<MainView>("MainView")!;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        void Pump() { for (var k = 0; k < 3; k++) { using var timeout = new CancellationTokenSource(20); Dispatcher.UIThread.MainLoop(timeout.Token); } }
        bool Busy() => (bool)typeof(MainView).GetField("_busy", flags)!.GetValue(view)!;
        void WaitFor(Func<bool> condition) { var end = DateTime.UtcNow.AddSeconds(10); while ((!condition() || Busy()) && DateTime.UtcNow < end) Pump(); Pump(); }
        void Open(string path) { var t = (Task)typeof(MainView).GetMethod("OpenPathAsync", flags)!.Invoke(view, [path])!; WaitFor(() => t.IsCompleted); t.GetAwaiter().GetResult(); }
        var ctrl = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
        void Key(Key k, RawInputModifiers m) { window.KeyPress(k, m, PhysicalKey.None, null); window.KeyRelease(k, m, PhysicalKey.None, null); Pump(); }
        var tabs = window.GetVisualDescendants().OfType<ListBox>().Single(x => x.Name == "Tabs");
        NoteDocument? Active() => tabs.SelectedItem as NoteDocument;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-viewerkeys-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            // Synthetic PDFs: a NUL-free text-like one and a compressed one with NUL bytes; both are binary by extension.
            var plainPdf = Path.Combine(root, "plain.pdf");
            File.WriteAllText(plainPdf, "%PDF-1.3\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n");
            var binaryPdf = Path.Combine(root, "stream.pdf");
            File.WriteAllBytes(binaryPdf, [.. "%PDF-1.3\n%\u00e2\u00e3\n4 0 obj << /Filter /FlateDecode >> stream\n"u8, 0x78, 0x9C, 0, 0, 1, 2, 0, 0xFF, .. "\nendstream\n%%EOF\n"u8]);
            var note = Path.Combine(root, "note.md");
            File.WriteAllText(note, "# note");
            Open(note);
            // MDHUB_EXTRA_PDF: optionally also check a real sample (opened read-only, never copied into the repo).
            var extra = Environment.GetEnvironmentVariable("MDHUB_EXTRA_PDF");
            foreach (var pdf in new[] { plainPdf, binaryPdf }.Concat(File.Exists(extra) ? [extra!] : Array.Empty<string>()))
            {
                var name = Path.GetFileName(pdf);
                // Opened from the editor (Ctrl+O, recent, drop): the editor hides and focus is gone.
                Active()!.Editor.Focus(); Pump();
                Open(pdf);
                Check(Active() is { IsBinary: true } d && d.Path == pdf, $"{name} opens as a placeholder tab");
                Check(!view.IsKeyboardFocusWithin, $"{name}: nothing inside the view keeps focus (the bug's trigger)");
                var count = tabs.ItemCount;
                Key(Avalonia.Input.Key.Tab, ctrl);
                Check(Active()?.Path != pdf, $"{name}: Ctrl+Tab switches tabs without focus");
                // Back from the note tab with the editor focused: switching to the PDF hides the editor and drops focus again.
                Active()!.Editor.Focus(); Pump();
                tabs.SelectedItem = tabs.Items.OfType<NoteDocument>().Single(x => x.Path == pdf); WaitFor(() => true);
                Check(!view.IsKeyboardFocusWithin, $"{name}: switching to the PDF tab leaves nothing focused");
                Key(Avalonia.Input.Key.W, ctrl); WaitFor(() => tabs.ItemCount < count);
                Check(tabs.ItemCount == count - 1 && tabs.Items.OfType<NoteDocument>().All(x => x.Path != pdf), $"{name}: Ctrl+W closes the PDF tab with nothing focused");
            }
            // Focus inside the view still goes through the normal handler exactly once.
            Open(plainPdf);
            view.FindControl<Control>("BinaryPlaceholder")!.GetVisualDescendants().OfType<Button>().First().Focus(); Pump();
            var before = tabs.ItemCount;
            Key(Avalonia.Input.Key.W, ctrl); WaitFor(() => tabs.ItemCount < before);
            Check(tabs.ItemCount == before - 1 && Active()?.Path == note, "Ctrl+W from the placeholder's button closes just that tab");
        }
        finally { window.Close(); try { Directory.Delete(root, true); } catch { } }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
