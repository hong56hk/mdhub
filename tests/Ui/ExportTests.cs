using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Media;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Services;
using Mdhub.Views;
using IOPath = System.IO.Path;

// Item 51: export a Markdown note to a standalone HTML file and to PDF (headless Edge/Chrome).
internal static class ExportTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Wait(Task t, int seconds = 10) { var end = DateTime.UtcNow.AddSeconds(seconds); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); }
        var dir = IOPath.Combine(IOPath.GetTempPath(), $"mdhub-export-{Guid.NewGuid():N}"); Directory.CreateDirectory(dir);
        File.WriteAllBytes(IOPath.Combine(dir, "dot.png"), Convert.FromBase64String(Png));
        var markdown = "# Sample note\n\nSome **bold**, *italic*, ~~struck~~ and `code`. A [link](https://example.com).\n\n- [x] done\n- [ ] todo\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n```csharp\n// comment\nvar s = \"text\"; int n = 42;\n```\n\n> quote\n\n![dot](dot.png) ![web](https://example.com/x.png)\n\n中文 日本語 한국어\n";
        var note = IOPath.Combine(dir, "Sample note.md"); File.WriteAllText(note, markdown);
        var w = new MainWindow { Width = 1300, Height = 700 };
        var startLanguage = Loc.Info.Code;
        try
        {
            // --- Exporter: HTML ---
            var html = MarkdownExporter.ToHtml(markdown, "Sample note", dir);
            Check(html.StartsWith("<!DOCTYPE html>", StringComparison.OrdinalIgnoreCase) && html.Contains("<meta charset=\"utf-8\"") && html.Contains("<style>") && !html.Contains("<link ") && !html.Contains("<script"), "HTML is a standalone document: inline CSS, no external stylesheet or script");
            Check(html.Contains("<title>Sample note</title>"), "The title is the note's name");
            var css = MarkdownExporter.Css();
            Check(MarkdownExporter.LightColours.All(kv => string.Equals(kv.Value, ThemeServiceLight(kv.Key), StringComparison.OrdinalIgnoreCase)), "Every CSS colour equals the Light theme resource");
            Check(MarkdownExporter.LightColours.Values.All(c => css.Contains(c, StringComparison.OrdinalIgnoreCase)), "All Light colours are used in the CSS");
            Check(html.Contains("<table>") && html.Contains("<th>A</th>") && html.Contains("<td>1</td>"), "Pipe tables are rendered");
            Check(html.Contains("type=\"checkbox\"") && html.Contains("checked=\"checked\""), "Task lists render as checkboxes");
            Check(html.Contains("<del>struck</del>") && html.Contains("<strong>bold</strong>") && html.Contains("<em>italic</em>"), "Bold, italic and strikethrough");
            Check(html.Contains("class=\"language-csharp\"") && html.Contains("<span class=\"tok-c\">// comment</span>") && html.Contains("<span class=\"tok-k\">var</span>") && html.Contains("<span class=\"tok-s\">&quot;text&quot;</span>") && html.Contains("<span class=\"tok-n\">42</span>"), "Code blocks are highlighted (comment, keyword, string, number)");
            Check(html.Contains("src=\"data:image/png;base64," + Png + "\"") && !html.Contains("src=\"dot.png\""), "Local images are embedded as base64");
            Check(html.Contains("src=\"https://example.com/x.png\"") && html.Contains("href=\"https://example.com\""), "Web images and links are left as they are");
            Check(html.Contains("中文 日本語 한국어"), "CJK text is kept");
            Check(css.Contains("size: A4") && css.Contains("break-inside: avoid") && css.Contains("table-header-group"), "Print CSS: A4, no splits inside code blocks/rows, repeated table headers");
            Check(MarkdownExporter.EmbedImage("missing.png", dir) is null && MarkdownExporter.EmbedImage("dot.png", null) is null && MarkdownExporter.EmbedImage("data:image/png;base64,AA", dir) is null, "Missing files, no base folder and data URLs are not rewritten");
            Check(MarkdownExporter.EmbedImage(new Uri(IOPath.Combine(dir, "dot.png")).AbsoluteUri, null)!.StartsWith("data:image/png;base64,"), "file:// image URLs are embedded");
            Check(MarkdownExporter.Highlight("<b>", "text") == "&lt;b&gt;" && MarkdownExporter.Highlight("SELECT 1 -- x", "sql").Contains("<span class=\"tok-c\">-- x</span>"), "Plain text is only escaped; SQL comments highlight");

            // --- Exporter: PDF ---
            MarkdownExporter.BrowserOverride = () => null;
            Exception? error = null; try { Task.Run(() => MarkdownExporter.ToPdfAsync(html, IOPath.Combine(dir, "x.pdf"))).GetAwaiter().GetResult(); } catch (Exception ex) { error = ex; }
            Check(error is IOException && error.Message == Loc.T("ExportNoBrowser") && !File.Exists(IOPath.Combine(dir, "x.pdf")), "No browser: a clean localized error and no file");
            MarkdownExporter.BrowserOverride = () => IOPath.Combine(dir, "not-a-browser.exe");
            error = null; try { Task.Run(() => MarkdownExporter.ToPdfAsync(html, IOPath.Combine(dir, "x.pdf"))).GetAwaiter().GetResult(); } catch (Exception ex) { error = ex; }
            Check(error is IOException && !File.Exists(IOPath.Combine(dir, "x.pdf")), "A browser that fails to run: a clean error and no file");
            MarkdownExporter.BrowserOverride = null;
            var hasBrowser = MarkdownExporter.FindBrowser() is not null;

            // --- UI ---
            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            var bar = w.GetVisualDescendants().OfType<TextFormatter>().Single();
            var status = w.GetVisualDescendants().OfType<BottomStatusBar>().Single();
            var toggle = bar.FindControl<Button>("TogglePreviewButton")!;
            var htmlButton = bar.FindControl<Button>("ExportHtmlButton")!; var pdfButton = bar.FindControl<Button>("ExportPdfButton")!;
            var row = (Panel)toggle.Parent!;
            var index = row.Children.IndexOf(toggle);
            Check(row.Children.IndexOf(htmlButton) == index + 1 && row.Children.IndexOf(pdfButton) == index + 2, "Order: preview toggle, HTML, PDF");
            Check(htmlButton.Bounds.Left > toggle.Bounds.Right - 0.5 && pdfButton.Bounds.Left > htmlButton.Bounds.Right - 0.5 && Math.Abs(htmlButton.Bounds.Height - toggle.Bounds.Height) < 0.5, "Export buttons sit right of the toggle with the same height");
            foreach (var b in new[] { htmlButton, pdfButton })
            {
                var paths = b.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().ToList();
                Check(paths.Count == 1 && paths[0].StrokeThickness <= 1.25 && paths[0].Bounds.Width <= 16 && paths[0].Bounds.Height <= 14 && !b.GetVisualDescendants().OfType<TextBlock>().Any(tb => !string.IsNullOrEmpty(tb.Text)), $"{b.Name}: thin-stroke icon, no text");
            }
            var pen = new Pen(Brushes.Black, 1.25);
            var g = ((Avalonia.Controls.Shapes.Path)htmlButton.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single()).Data!;
            // Bug 82: 20f95af restored the item 51 '</>' icon (bug 73's '<html>' was reverted on purpose).
            Check(g.StrokeContains(pen, new Point(1.5, 7)) && g.StrokeContains(pen, new Point(14.5, 7)) && g.StrokeContains(pen, new Point(8, 7)), "HTML icon is '</>'");
            g = pdfButton.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single().Data!;
            Check(g.StrokeContains(pen, new Point(9.5, 3)) && g.StrokeContains(pen, new Point(11, 4.5)) && g.StrokeContains(pen, new Point(3.5, 12)), "PDF icon is a page with a folded corner");
            foreach (var code in Loc.Languages.Select(l => l.Code))
            {
                Loc.Set(code); Pump();
                var table = Loc.Table(code);
                Check(ToolTip.GetTip(htmlButton) as string == table["ExportHtmlTip"] && ToolTip.GetTip(pdfButton) as string == table["ExportPdfTip"] && table.ContainsKey("MenuExport") && table.ContainsKey("Exported") && table.ContainsKey("ExportOpen") && table.ContainsKey("ExportNoBrowser") && table.ContainsKey("ExportPdfFailed"), $"{code}: localized tooltips and export strings");
            }
            Loc.Set("en"); Pump();
            var menu = w.GetVisualDescendants().OfType<TopMenu>().Single();
            var exportMenu = menu.FindControl<MenuItem>("ExportMenu")!;
            Check(exportMenu.Parent is MenuItem file && (string?)file.Header == Loc.T("MenuFile") && (string?)exportMenu.Header == "Export" && exportMenu.Items.OfType<MenuItem>().Select(i => i.Header as string).SequenceEqual(["HTML\u2026", "PDF\u2026"]), "File > Export > HTML… / PDF…");

            // Untitled tab is Markdown; open an image to check the disabled state.
            File.WriteAllBytes(IOPath.Combine(dir, "pic.png"), Convert.FromBase64String(Png));
            Wait((Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(v, [IOPath.Combine(dir, "pic.png")])!);
            Check(!htmlButton.IsEnabled && !pdfButton.IsEnabled, "Disabled for a non-Markdown file");
            Wait((Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(v, [note])!);
            Check(htmlButton.IsEnabled && pdfButton.IsEnabled, "Enabled for a Markdown note");

            string? suggested = null;
            var picker = typeof(MainView).GetProperty("ExportPickerOverride", Flags)!;
            picker.SetValue(v, (Func<string, string, Task<string?>>)((s, ext) => { suggested = s; return Task.FromResult<string?>(IOPath.ChangeExtension(s, ext)); }));
            string? opened = null;
            typeof(MainView).GetProperty("OpenFileOverride", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, (Action<string>)(p => opened = p));
            var export = typeof(MainView).GetMethod("ExportAsync", Flags)!;
            Wait((Task)export.Invoke(v, [false])!);
            var htmlOut = IOPath.Combine(dir, "Sample note.html");
            Check(suggested == htmlOut, "The Save dialog suggests <note>.html in the note's folder");
            Check(File.Exists(htmlOut) && File.ReadAllText(htmlOut).Contains("data:image/png;base64,"), "HTML export writes the file with embedded images");
            var statusText = status.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "Status").Text;
            Check(statusText == Loc.F("Exported", htmlOut) && status.ActionLabel == "Open", "Status: 'Exported <path>' with an Open action");
            status.InvokeAction();
            Check(opened == htmlOut, "Open opens the exported file");
            Console.WriteLine($"INFO sample note {new FileInfo(note).Length} B -> HTML {new FileInfo(htmlOut).Length} B");

            picker.SetValue(v, (Func<string, string, Task<string?>>)((s, ext) => Task.FromResult<string?>(null)));
            File.Delete(htmlOut);
            Wait((Task)export.Invoke(v, [false])!);
            Check(!File.Exists(htmlOut) && opened == htmlOut, "Cancelling the dialog writes nothing");

            if (hasBrowser)
            {
                picker.SetValue(v, (Func<string, string, Task<string?>>)((s, ext) => { suggested = s; return Task.FromResult<string?>(s); }));
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Wait((Task)export.Invoke(v, [true])!, 120);
                var pdfOut = IOPath.Combine(dir, "Sample note.pdf");
                var head = File.Exists(pdfOut) ? File.ReadAllBytes(pdfOut) : [];
                Check(suggested == pdfOut && head.Length > 1000 && System.Text.Encoding.ASCII.GetString(head, 0, 5) == "%PDF-", "PDF export (headless browser) writes a valid PDF");
                var text = System.Text.Encoding.Latin1.GetString(head);
                Check(text.Contains("/URI") && text.Contains("example.com"), "The PDF keeps the link");
                Check(text.Contains("/MediaBox [0 0 594.95996 841.91998]") || text.Contains("/MediaBox [0 0 595"), "The PDF page is A4");
                Check(status.ActionLabel == "Open", "PDF status has the Open action");
                Console.WriteLine($"INFO PDF {head.Length} B in {sw.ElapsedMilliseconds} ms");
            }
            else Console.WriteLine("SKIP PDF export: no Edge/Chrome");
            Console.WriteLine($"Export tests: {passed} passed");
        }
        finally
        {
            MarkdownExporter.BrowserOverride = null;
            Loc.Set(startLanguage);
            w.Close();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    static string ThemeServiceLight(string key)
    {
        var c = ThemeService.Lookup(key, AppTheme.Light);
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}