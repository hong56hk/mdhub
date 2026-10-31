using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;
using Path = Avalonia.Controls.Shapes.Path;

// Item 50: thin-stroke icons for the Link, Bullet list, Ordered list and Table format buttons.
internal static class FormatIconTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(10); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); }
        var note = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mdhub-icons-{Guid.NewGuid():N}.md"); File.WriteAllText(note, "");
        var w = new MainWindow { Width = 1300, Height = 700 };
        var startLanguage = Loc.Info.Code;
        try
        {
            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            var bar = w.GetVisualDescendants().OfType<TextFormatter>().Single();
            Wait((Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(v, [note])!);
            var buttons = bar.FindControl<StackPanel>("FormatBar")!.Children.OfType<Button>().ToList();
            Button B(string tag) => buttons.Single(b => b.Tag as string == tag);
            var bold = B("bold");
            var specs = new (string Tag, string Key, string[] Paths)[]
            {
                ("link", "InsertLink", ["LinkIcon"]), ("list", "BulletList", ["BulletIcon", "BulletDots"]),
                ("ordered", "NumberedList", ["OrderedIcon", "OrderedDigits"]), ("table", "InsertTable", ["TableIcon", "TableHeader"]),
            };
            foreach (var (tag, key, names) in specs)
            {
                var button = B(tag);
                var paths = button.GetVisualDescendants().OfType<Path>().ToList();
                Check(button.Content is Control && !button.GetVisualDescendants().OfType<TextBlock>().Any(tb => !string.IsNullOrEmpty(tb.Text)) && names.All(n => paths.Any(p => p.Name == n)),
                    $"{tag}: the button is icon-only ({string.Join(", ", names)})");
                Check(paths.Where(p => p.Stroke is not null).All(p => p.StrokeThickness <= 1.25) && paths.All(p => p.Bounds.Width <= 16 && p.Bounds.Height <= 14), $"{tag}: thin strokes in a 16x14 box like the toggle icon");
                Check(button.Bounds.Height == bold.Bounds.Height && button.Bounds.Width >= 32 && Math.Abs(button.Bounds.Width - bold.Bounds.Width) <= 2, $"{tag}: same size as the text buttons ({button.Bounds.Size} vs {bold.Bounds.Size})");
            }
            Check(buttons.Zip(buttons.Skip(1)).All(p => Math.Abs(p.Second.Bounds.Left - p.First.Bounds.Right - 2) < 0.01), "Spacing between all format buttons is unchanged (2px)");

            // Shapes: sample a few points of each geometry.
            var pen = new Pen(Brushes.Black, 1.25);
            bool On(string name, params (double X, double Y)[] pts) => w.GetVisualDescendants().OfType<Path>().Single(p => p.Name == name).Data!.StrokeContains(pen, default) is var _ && pts.All(pt => w.GetVisualDescendants().OfType<Path>().Single(p => p.Name == name).Data!.StrokeContains(pen, new Point(pt.X, pt.Y)));
            bool Off(string name, params (double X, double Y)[] pts) => pts.All(pt => !w.GetVisualDescendants().OfType<Path>().Single(p => p.Name == name).Data!.StrokeContains(pen, new Point(pt.X, pt.Y)));
            Check(On("LinkIcon", (3.5, 4.5), (12.5, 9.5), (1, 7), (15, 7)) && Off("LinkIcon", (8, 1), (8, 13)), "Link: two horizontal chain links side by side");
            var dots = w.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "BulletDots").Data!;
            Check(On("BulletIcon", (10, 2.5), (10, 7), (10, 11.5)) && Off("BulletIcon", (4.5, 7), (10, 4.75), (2.4, 7)) && new[] { 2.5, 7, 11.5 }.All(y => dots.FillContains(new Point(2.4, y))) && !dots.FillContains(new Point(2.4, 4.75)) && dots.Bounds.Right < 5, "Bullet list: three lines with a dot left of each");
            Check(On("OrderedIcon", (10, 2.5), (10, 7), (10, 11.5)) && w.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "OrderedDigits").Data!.Bounds is { Right: < 5, Height: > 11 }, "Ordered list: three lines with tiny 1, 2, 3 on their left");
            // Bug 65: a 3x3 grid (two inner row lines, two inner column lines), same 16x14 box and stroke.
            var grid = w.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "TableIcon").Data!;
            double[] rows = [1.5, 5.17, 8.83, 12.5], cols = [1.5, 5.83, 10.17, 14.5];
            static bool Even(double[] e) => Enumerable.Range(0, 3).All(k => Math.Abs((e[k + 1] - e[k]) - (e[3] - e[0]) / 3) < 0.02);
            Check(On("TableIcon", (8, 1.5), (8, 12.5), (1.5, 7), (14.5, 7), (3.5, rows[1]), (12, rows[2]), (cols[1], 3.5), (cols[2], 10.5))
                && Off("TableIcon", (3.6, 7), (8, 7), (12.4, 7), (3.6, 3.3), (8, 10.7), (12.4, 10.7)) && Even(rows) && Even(cols)
                && grid.Bounds is { X: >= 1, Y: >= 1, Right: <= 15, Bottom: <= 13 }, "Table: rounded rectangle split into an even 3x3 grid");

            // Colours follow the theme: the same foreground as the text buttons, also when disabled.
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
            {
                ThemeService.Apply(theme, persist: false); Pump();
                var text = ((ISolidColorBrush)bold.Foreground!).Color;
                foreach (var (tag, _, _) in specs)
                {
                    var brushes = B(tag).GetVisualDescendants().OfType<Path>().Select(p => p.Stroke ?? p.Fill).ToList();
                    Check(brushes.All(b => b is ISolidColorBrush s && s.Color == text), $"{theme}: {tag} icon uses the format-bar text colour {text}");
                }
            }
            // Bug 65: when the format bar is disabled (active tab not .md) the icons dim exactly like the text buttons, in both themes.
            var txt = System.IO.Path.ChangeExtension(note, ".txt"); File.WriteAllText(txt, "plain");
            try
            {
                Color Presenter(Button b) => ((ISolidColorBrush)b.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First().Foreground!).Color;
                Color Res(string key, AppTheme theme) => bold.TryFindResource(key, theme == AppTheme.Dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light, out var r) && r is ISolidColorBrush s ? s.Color : default;
                void Icons(string state, AppTheme theme, Color expect)
                {
                    foreach (var (tag, _, _) in specs)
                    {
                        var brushes = B(tag).GetVisualDescendants().OfType<Path>().Select(p => p.Stroke ?? p.Fill).ToList();
                        Check(brushes.Count > 0 && brushes.All(b => b is ISolidColorBrush s && s.Color == expect) && Presenter(B(tag)) == expect, $"{theme} {state}: {tag} icon strokes are {expect}, like the text buttons");
                    }
                }
                // Bug 73/82: the export icons (HTML is '</>' again since 20f95af) dim with the bar too.
                var exportHtml = bar.FindControl<Button>("ExportHtmlButton")!; var exportPdf = bar.FindControl<Button>("ExportPdfButton")!;
                void Export(AppTheme theme, Color expect)
                {
                    foreach (var eb in new[] { exportHtml, exportPdf })
                    {
                        var ip = eb.GetVisualDescendants().OfType<Path>().Single();
                        Check(ip.Stroke is ISolidColorBrush s && s.Color == expect && Presenter(eb) == expect && ip.StrokeThickness <= 1.25, $"{theme}: {eb.Name} icon stroke is {expect}");
                    }
                }
                Check(exportHtml.Bounds.Height == exportPdf.Bounds.Height && exportHtml.Bounds.Width >= exportPdf.Bounds.Width && exportHtml.Bounds.Width <= 40 && exportHtml.GetVisualDescendants().OfType<Path>().Single().Bounds.Height == 14,
                    $"</> export button: same height as PDF, reasonable width ({exportHtml.Bounds.Size} vs {exportPdf.Bounds.Size})");
                foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
                {
                    ThemeService.Apply(theme, persist: false); Pump();
                    Wait((Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(v, [txt])!);
                    var disabled = Res("ButtonForegroundDisabled", theme);
                    Check(!bold.IsEffectivelyEnabled && specs.All(s => !B(s.Tag).IsEffectivelyEnabled) && Presenter(bold) == disabled && disabled != default, $"{theme}: a .txt tab disables the format bar (text buttons {disabled})");
                    Icons("disabled", theme, disabled); Export(theme, disabled);
                    Wait((Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(v, [note])!);
                    var normal = Presenter(bold);
                    Check(bold.IsEffectivelyEnabled && normal != disabled && normal == ((ISolidColorBrush)bold.Foreground!).Color, $"{theme}: back on the .md tab the format bar is enabled (text buttons {normal})");
                    Icons("enabled", theme, normal); Export(theme, normal);
                }
            }
            finally
            {
                var txtDoc = w.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "Tabs").Items.OfType<NoteDocument>().FirstOrDefault(d => d.Path == txt);
                if (txtDoc is not null) Wait((Task)typeof(MainView).GetMethod("CloseDocumentAsync", Flags)!.Invoke(v, [txtDoc])!);
                Pump(); File.Delete(txt);
            }
            ThemeService.Apply(AppTheme.Dark, persist: false); Pump();

            // Crisp at every zoom: the icons are vectors under the window's LayoutTransform, and lines sit on pixel centres at 100%.
            var link = w.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "LinkIcon");
            foreach (var zoom in new[] { 100, 150, 200 })
            {
                w.SetZoom(zoom, announce: false); Pump();
                var a = link.TranslatePoint(new Point(0, 0), w)!.Value; var b = link.TranslatePoint(new Point(16, 0), w)!.Value;
                using var frame = w.CaptureRenderedFrame()!;
                Check(Math.Abs((b.X - a.X) - 16 * zoom / 100.0) < 0.01 && frame is not null, $"{zoom}%: the icon scales as a vector ({b.X - a.X:0.#}px wide)");
            }
            w.SetZoom(100, announce: false); Pump();

            // Tooltips and accessibility names in every UI language.
            foreach (var code in Loc.Languages.Select(l => l.Code))
            {
                Loc.Set(code); Pump();
                var linkTip = ToolTip.GetTip(B("link")) as string;
                var sc = Shortcuts.Format("editor.link", ShortcutKeys.IsMac);
                Check(linkTip == $"{Loc.T("InsertLink")} ({sc})" && (code != "en" || linkTip == "Insert link (Ctrl+L)"), $"{code}: link tooltip \"{linkTip}\" names its shortcut");
                foreach (var (tag, key, _) in specs)
                {
                    var name = AutomationProperties.GetName(B(tag));
                    Check(name == Loc.T(key) && name is { Length: > 0 } && (tag == "link" || ToolTip.GetTip(B(tag)) as string == Loc.T(key)) && (code == "en" || name != Loc.Table("en")![key]),
                        $"{code}: {tag} has the localized name and tooltip \"{name}\"");
                }
            }
            Loc.Set("en"); Pump();

            // The buttons still apply their formatting.
            var doc = (NoteDocument)w.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "Tabs").SelectedItem!;
            foreach (var (tag, expect) in new[] { ("list", "- "), ("ordered", "1. "), ("link", "]("), ("table", "|") })
            {
                doc.Editor.Text = "item"; doc.Editor.SelectionStart = 0; doc.Editor.SelectionEnd = 4; Pump();
                B(tag).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                Check(doc.Editor.Text!.Contains(expect), $"{tag}: clicking still formats the note ({doc.Editor.Text!.Replace("\n", "\\n").Replace("\r", "")})");
            }
        }
        finally
        {
            Loc.Set(startLanguage);
            ThemeService.Apply(AppTheme.Dark, persist: false);
            try { w.Close(); Pump(); } catch { }
            File.Delete(note);
        }
        Console.WriteLine($"Format icon tests: {passed} passed");
    }
}