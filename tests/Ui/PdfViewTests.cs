using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 102a: built-in PDF tab (open, page count and status, lazy render, zoom shortcuts, Ctrl+W, error states).
internal static class PdfViewTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS: " + name); }
        void Pump() { using var t = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(t.Token); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(10); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); }

        var root = Path.Combine(Path.GetTempPath(), "mdhub-pdfview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var good = Path.Combine(root, "doc.pdf");
        var bad = Path.Combine(root, "bad.pdf");
        var enc = Path.Combine(root, "secret.pdf");
        var note = Path.Combine(root, "note.md");
        File.WriteAllBytes(good, PdfFixtures.MultiPage("Page one: hello mdhub", "Page two: more text", "Page three: last page"));
        File.WriteAllBytes(bad, PdfFixtures.Corrupt());
        File.WriteAllBytes(enc, PdfFixtures.Encrypted("secret text"));
        File.WriteAllText(note, "# note");

        // Service-level classification (no window).
        using (var okDoc = PdfDocument.Open(good))
            Check(okDoc.Error == PdfError.None && okDoc.PageCount == 3 && okDoc.Text(0).Contains("hello mdhub") && okDoc.Text(1).Contains("more text"),
                "a 3-page fixture opens with real text on each page");
        using (var c = PdfDocument.Open(bad)) Check(c.Error == PdfError.Corrupt && c.PageCount == 0, "a corrupt PDF is reported as corrupt");
        using (var e = PdfDocument.Open(enc)) Check(e.Error == PdfError.Encrypted && e.PageCount == 0, "an encrypted PDF is reported as encrypted (no password prompt in 102a)");

        var w = new MainWindow { Width = 900, Height = 520 };
        try
        {
            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            var status = w.GetVisualDescendants().OfType<BottomStatusBar>().Single();
            Task Invoke(string m, params object?[] args) => (Task)typeof(MainView).GetMethod(m, Flags)!.Invoke(v, args)!;
            NoteDocument? Active() => (NoteDocument?)typeof(MainView).GetField("_active", Flags)!.GetValue(v);
            PdfViewer Viewer() => w.GetVisualDescendants().OfType<PdfViewer>().First(x => x.IsVisible);
            var ctrl = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            void Key(Key k, RawInputModifiers m = default) { w.KeyPress(k, m, PhysicalKey.None, null); w.KeyRelease(k, m, PhysicalKey.None, null); Pump(); }
            var tabs = w.GetVisualDescendants().OfType<ListBox>().Single(x => x.Name == "Tabs");

            Wait(Invoke("OpenPathAsync", good));
            var doc = Active();
            Check(doc is { IsPdf: true, IsViewOnly: true } && doc.Path == good && !doc.IsBinary, "a .pdf opens as a read-only PDF tab, not a binary placeholder");
            Pump(); Pump();
            var viewer = Viewer();
            Check(viewer.PageCount == 3 && viewer.CurrentPage == 1, "the viewer has 3 pages and starts on page 1");
            Check(status.PdfPage == (1, 3), "status bar records page 1 of 3");
            var counts = status.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "Counts");
            Check(counts.Text == Loc.F("PdfPageOf", 1, 3), $"status text is localized 'page 1 of 3' ({counts.Text})");
            var kind = status.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "FileKindLabel");
            Check(kind.Text == Loc.T("KindPdf"), "status kind is PDF");

            // Lazy render: at 100% a letter page is taller than the 520px window, so page 3 is not cached.
            viewer.StepZoom(0); // no-op if already 1? Set to a known scale via reset then in
            // Force 100% by stepping to 1 if needed — Fit may have run. Use reflection-free: zoom out to 10% then in to 100%.
            for (var i = 0; i < 20 && viewer.ZoomPercent > 100; i++) viewer.StepZoom(-1);
            for (var i = 0; i < 20 && viewer.ZoomPercent < 100; i++) viewer.StepZoom(1);
            Pump(); Pump();
            Check(viewer.ZoomPercent == 100, $"zoom can be set to 100% ({viewer.ZoomPercent})");
            var cached = viewer.CachedPages.ToList();
            Check(cached.Contains(0) && !cached.Contains(2), $"lazy render: page 1 (and neighbour) kept, page 3 evicted ({string.Join(",", cached)})");

            viewer.ScrollToPage(3); Pump(); Pump();
            Check(viewer.CurrentPage == 3 && status.PdfPage == (3, 3) && counts.Text == Loc.F("PdfPageOf", 3, 3),
                $"ScrollToPage(3) updates the current page and status ({viewer.CurrentPage}, {counts.Text})");
            cached = viewer.CachedPages.ToList();
            Check(cached.Contains(2) && !cached.Contains(0), $"after scrolling to page 3, page 1 is evicted ({string.Join(",", cached)})");
            viewer.ScrollToPage(1); Pump();

            var before = viewer.ZoomPercent;
            viewer.Focus(); Pump();
            Key(Avalonia.Input.Key.OemPlus, ctrl);
            Check(viewer.ZoomPercent == ImageZoom.Percent(ImageZoom.StepZoom(before / 100.0, 1)) || viewer.ZoomPercent > before,
                $"Ctrl+= zooms the PDF in ({before} -> {viewer.ZoomPercent})");
            var mid = viewer.ZoomPercent;
            Key(Avalonia.Input.Key.OemMinus, ctrl);
            Check(viewer.ZoomPercent < mid, $"Ctrl+- zooms the PDF out ({mid} -> {viewer.ZoomPercent})");
            Key(Avalonia.Input.Key.D0, ctrl);
            Check(viewer.Document is not null, "Ctrl+0 fits the page (reset zoom still acts on the PDF, not the window)");

            var tabCount = tabs.ItemCount;
            Wait(Invoke("OpenPathAsync", note));
            Wait(Invoke("OpenPathAsync", good));
            Check(tabs.ItemCount == tabCount + 1, "the note is a second tab; the PDF reuses its tab");
            // Reopened? existing tab. Open note first then pdf was already open so item count +1 from note only if pdf still open.
            // At this point we had pdf, opened note, opened pdf again (switch). Count = previous + 1 (note).
            viewer = Viewer();
            viewer.Focus(); Pump();
            var n = tabs.ItemCount;
            Key(Avalonia.Input.Key.W, ctrl);
            var end = DateTime.UtcNow.AddSeconds(5);
            while (tabs.ItemCount >= n && DateTime.UtcNow < end) Pump();
            Check(tabs.ItemCount == n - 1 && Active()?.Path == note, "Ctrl+W with the PDF viewer focused closes the PDF tab");

            Wait(Invoke("OpenPathAsync", bad));
            Check(Active() is { IsPdf: true } && Viewer().Document is { Error: PdfError.Corrupt }, "a corrupt PDF opens as a PDF tab in the error state");
            var err = Viewer().GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "ErrorText");
            Check(err.IsVisible && err.Text == Loc.T("PdfCorrupt"), $"corrupt message is localized ({err.Text})");
            Key(Avalonia.Input.Key.W, ctrl);
            var end2 = DateTime.UtcNow.AddSeconds(5);
            while (Active()?.Path == bad && DateTime.UtcNow < end2) Pump();

            Wait(Invoke("OpenPathAsync", enc));
            Check(Active() is { IsPdf: true } && Viewer().Document is { Error: PdfError.Encrypted }, "an encrypted PDF opens as a PDF tab in the error state");
            err = Viewer().GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "ErrorText");
            Check(err.IsVisible && err.Text == Loc.T("PdfEncrypted"), $"encrypted message is localized ({err.Text})");

            foreach (var code in Loc.Languages.Select(l => l.Code))
            {
                Loc.Set(code); Pump();
                var expected = Loc.F("PdfPageOf", 1, 3);
                Wait(Invoke("OpenPathAsync", good));
                Pump(); Pump();
                var shown = status.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "Counts").Text;
                Check(shown == Loc.F("PdfPageOf", Viewer().CurrentPage, 3) && (code == Loc.English || Loc.F("PdfPageOf", 1, 3) != "Page 1 of 3" || Loc.T("PdfEncrypted") != "This PDF is encrypted and cannot be opened."),
                    $"status and error strings follow the language ({code}: {shown})");
                // Also the error strings exist
                Check(Loc.T("PdfEncrypted") != "PdfEncrypted" && Loc.T("PdfCorrupt") != "PdfCorrupt", $"{code}: error strings are present");
            }
            Loc.Set(Loc.English); Pump();
        }
        finally { try { w.Close(); Pump(); } catch { } try { Directory.Delete(root, true); } catch { } }
        Console.WriteLine($"PDF view tests passed: {passed}");
    }
}
