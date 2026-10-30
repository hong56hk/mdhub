using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;
using IOPath = System.IO.Path;
using ShapePath = Avalonia.Controls.Shapes.Path;

// Item 60: the editor and preview settings gears live in the toolbar right after Export PDF.
internal static class GearsTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Settle() { for (var i = 0; i < 8; i++) Pump(); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(15); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Settle(); }
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var sessionPath = IOPath.Combine(IOPath.GetTempPath(), $"mdhub-gears-{Guid.NewGuid():N}.json");
        SessionManager.PathOverride = sessionPath; SessionManager.Enabled = true;
        EditorFonts.Import(null); PreviewSettings.Import(null);
        var root = IOPath.Combine(IOPath.GetTempPath(), $"mdhub-gears-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        string F(string name, byte[] bytes) { var p = IOPath.Combine(root, name); File.WriteAllBytes(p, bytes); return p; }
        var w = new MainWindow { Width = 1300, Height = 800 };
        var startLanguage = Loc.Current;
        try
        {
            w.Show(); Pump();
            var view = w.GetVisualDescendants().OfType<MainView>().Single();
            var bar = w.GetVisualDescendants().OfType<TextFormatter>().Single();
            var tabs = w.GetVisualDescendants().OfType<FileTabs>().Single().FindControl<ListBox>("Tabs")!;
            void Open(string path) => Wait((Task)typeof(MainView).GetMethod("OpenPathAsync", flags)!.Invoke(view, [path])!);
            var mdDoc = (NoteDocument)typeof(MainView).GetMethod("AddDocument", flags)!.Invoke(view, [null, "# Gears\n\ntext", "gears.md", null])!;
            Open(F("notes.txt", System.Text.Encoding.UTF8.GetBytes("plain text\n")));
            var txtDoc = (NoteDocument)tabs.SelectedItem!;
            Open(F("data.json", System.Text.Encoding.UTF8.GetBytes("{ \"a\": 1 }\n")));
            var jsonDoc = (NoteDocument)tabs.SelectedItem!;
            Open(F("pic.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==")));
            var pngDoc = (NoteDocument)tabs.SelectedItem!;
            Open(F("blob.dat", [(byte)'a', 0, 1, 2, (byte)'b']));
            var binDoc = (NoteDocument)tabs.SelectedItem!;
            Check(!txtDoc.IsMarkdown && !txtDoc.IsViewOnly && !jsonDoc.IsMarkdown && !jsonDoc.IsViewOnly && pngDoc.IsImage && binDoc.IsBinary && mdDoc.IsMarkdown, "Opened a Markdown, a .txt, a .json, an image and a binary tab");
            void Activate(NoteDocument d) { tabs.SelectedItem = d; Settle(); }
            Activate(mdDoc);

            var editorGear = bar.FindControl<Button>("EditorSettingsButton")!;
            var previewGear = bar.FindControl<Button>("PreviewSettingsButton")!;
            var pdf = bar.FindControl<Button>("ExportPdfButton")!;
            var html = bar.FindControl<Button>("ExportHtmlButton")!;

            // ---- Order: right after Export PDF, editor gear first ----
            var group = (StackPanel)pdf.Parent!;
            var row = group.Children.ToList();
            var at = row.IndexOf(pdf);
            Check(at >= 0 && row.IndexOf(editorGear) == at + 1 && row.IndexOf(previewGear) == at + 2 && row.Count == at + 3, "Toolbar order: ... Export PDF, editor gear, preview gear (last)");
            double X(Control c) => c.TranslatePoint(default, bar)!.Value.X;
            Check(X(html) < X(pdf) && X(pdf) < X(editorGear) && X(editorGear) < X(previewGear), $"Laid out left to right (PDF {X(pdf):F0}, editor {X(editorGear):F0}, preview {X(previewGear):F0})");
            Check(editorGear.Bounds.Height == pdf.Bounds.Height && previewGear.Bounds.Height == pdf.Bounds.Height && Math.Abs(editorGear.Bounds.Width - pdf.Bounds.Width) < 0.5 && Math.Abs(previewGear.Bounds.Width - pdf.Bounds.Width) < 0.5,
                $"Gears are the size of the export buttons ({editorGear.Bounds.Size}, {pdf.Bounds.Size})");
            var md = w.GetVisualDescendants().OfType<MdEditor>().Single(); var editor = w.GetVisualDescendants().OfType<TextEditor>().Single();
            Check(!md.GetVisualDescendants().OfType<Button>().Any(b => b.Name is "PreviewSettingsButton") && editor.FindControl<Button>("EditorFontsButton") is null
                && w.GetVisualDescendants().OfType<Button>().Count(b => b.Name is "PreviewSettingsButton" or "EditorSettingsButton") == 2, "The corner gears are gone (only the two toolbar gears remain)");

            // ---- Icons: distinct thin-stroke resources, toolbar size and colours ----
            var editorIcon = bar.FindControl<ShapePath>("EditorSettingsIcon")!; var previewIcon = bar.FindControl<ShapePath>("PreviewSettingsIcon")!; var pdfIcon = bar.FindControl<ShapePath>("ExportPdfIcon")!;
            Check(editorGear.Content == editorIcon && previewGear.Content == previewIcon, "Each gear's content is its icon");
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                var eg = Application.Current!.TryGetResource("EditorSettingsIconGeometry", variant, out var e) ? e as Geometry : null;
                var pg = Application.Current!.TryGetResource("PreviewSettingsIconGeometry", variant, out var p) ? p as Geometry : null;
                Check(eg is not null && pg is not null && !ReferenceEquals(eg, pg) && eg.Bounds != pg.Bounds, $"Both icon geometries are theme resources, distinct ({variant})");
            }
            Check(editorIcon.Data == Application.Current!.FindResource("EditorSettingsIconGeometry") && previewIcon.Data == Application.Current!.FindResource("PreviewSettingsIconGeometry"), "Icons use the resource geometries");
            var theme = File.ReadAllText(IOPath.Combine(RepoRoot(), "Styles", "Theme.axaml"));
            string Geo(string key) => Regex.Match(theme, $"x:Key=\"{key}\">([^<]*)<").Groups[1].Value;
            var gear = Geo("EditorSettingsIconGeometry").Split(" M 10,")[0];
            Check(gear.Length > 50 && Geo("PreviewSettingsIconGeometry").StartsWith(gear) && Geo("EditorSettingsIconGeometry").Contains("H 15 M 12.5,8.75 V 13.5") && Geo("PreviewSettingsIconGeometry").Contains("Q 12.25,8.25"),
                "Same gear, different badge: a 'T' (text) for the editor, an eye for the preview");
            Check(new[] { editorIcon, previewIcon }.All(i => i.Width == pdfIcon.Width && i.Height == pdfIcon.Height && i.StrokeThickness == pdfIcon.StrokeThickness && i.Fill is null
                && i.StrokeLineCap == PenLineCap.Round && i.Data!.Bounds.Right <= 16 && i.Data.Bounds.Bottom <= 14 && i.Data.Bounds.Left >= 0 && i.Data.Bounds.Top >= 0), "Icons match the toolbar icons: 16x14, 1.25 px round strokes, no fill, inside the box");
            foreach (var t in new[] { AppTheme.Light, AppTheme.Dark })
            {
                ThemeService.Apply(t, persist: false); Settle();
                Check(new[] { (editorGear, editorIcon), (previewGear, previewIcon) }.All(x => x.Item2.Stroke is ISolidColorBrush s && x.Item1.Foreground is ISolidColorBrush f && s.Color == f.Color
                    && pdfIcon.Stroke is ISolidColorBrush ps && ps.Color == s.Color), $"Icons use the toolbar icon colour in {t}");
            }
            Check(!Regex.IsMatch(string.Join("\n", File.ReadAllLines(IOPath.Combine(RepoRoot(), "Components", "TextFormatter.axaml")).Where(l => l.Contains("Settings"))), "#[0-9A-Fa-f]{3,8}\\b|Brushes\\."), "No hardcoded colours on the gears");

            // ---- Tooltips and accessible names, every language ----
            foreach (var language in Loc.Languages)
            {
                Loc.Set(language.Code); Settle();
                var table = Loc.Table(language.Code);
                var es = table.TryGetValue("EditorSettings", out var a) ? a : null; var ps = table.TryGetValue("PreviewSettings", out var b) ? b : null;
                Check(!string.IsNullOrWhiteSpace(es) && !string.IsNullOrWhiteSpace(ps) && es != ps
                    && ToolTip.GetTip(editorGear) as string == es && AutomationProperties.GetName(editorGear) == es && ToolTip.GetTip(previewGear) as string == ps && AutomationProperties.GetName(previewGear) == ps,
                    $"{language.Code}: tooltips and names '{es}' / '{ps}'");
            }
            Loc.Set("en"); Settle();
            Check(Loc.T("EditorSettings") == "Editor settings" && Loc.T("PreviewSettings") == "Preview settings" && Loc.Languages.Count == 8, "English: 'Editor settings' and 'Preview settings' (8 languages present)");

            // ---- Visibility per tab type ----
            (bool, bool) Shown(NoteDocument d) { Activate(d); return (editorGear.IsVisible, previewGear.IsVisible); }
            Check(Shown(mdDoc) == (true, true), "Markdown tab: both gears");
            Check(Shown(txtDoc) == (true, false), "Text tab (.txt): editor gear only");
            Check(Shown(jsonDoc) == (true, false), "Other text tab (.json): editor gear only");
            Check(Shown(pngDoc) == (false, false), "Image tab: no gears");
            Check(Shown(binDoc) == (false, false), "Binary tab: no gears");
            Check(Shown(mdDoc) == (true, true), "Back to Markdown: both gears again");
            var untitled = (NoteDocument)typeof(MainView).GetMethod("AddDocument", flags)!.Invoke(view, [null, "", null, null])!; Settle();
            Check((editorGear.IsVisible, previewGear.IsVisible) == (untitled.IsMarkdown, untitled.IsMarkdown) || (editorGear.IsVisible && previewGear.IsVisible == untitled.IsMarkdown), "A new untitled tab follows its type");

            // ---- Flyouts: same contents, anchored below their own button ----
            Activate(mdDoc);
            void Click(Button b) { typeof(Button).GetMethod("OnClick", flags)!.Invoke(b, null); Settle(); }
            foreach (var (button, panelName, other) in new[] { (editorGear, "EditorFontsPanel", previewGear), (previewGear, "", editorGear) })
            {
                var flyout = (Flyout)button.Flyout!;
                Click(button);
                Check(flyout.IsOpen && flyout.Target == button && flyout.Placement == PlacementMode.BottomEdgeAlignedRight && !((Flyout)other.Flyout!).IsOpen, $"{button.Name}: a click opens its own flyout, below the button (right-aligned)");
                var content = (Control)flyout.Content!;
                Check(panelName != "" ? content.Name == panelName && content.GetLogicalDescendants().OfType<ComboBox>().Count() == 2
                    : new[] { "BodyFontBox", "BodySizeBox", "CodeFontBox", "CodeSizeBox", "LineHeightBox", "ParagraphSpacingBox", "ResetPreviewSettings" }.All(n => content.GetLogicalDescendants().OfType<Control>().Any(c => c.Name == n)),
                    $"{button.Name}: the flyout keeps its contents");
                var popup = TopLevel.GetTopLevel(content);
                var below = button.PointToScreen(new Point(button.Bounds.Width, button.Bounds.Height));
                if (popup is not null && popup != w)
                {
                    var origin = popup.PointToScreen(default);
                    var right = popup.PointToScreen(new Point(popup.Bounds.Width, 0));
                    Check(origin.Y >= below.Y - 1 && Math.Abs(right.X - below.X) <= 2, $"{button.Name}: the popup sits under the button, right edges aligned (popup {origin}..{right}, button bottom-right {below})");
                }
                else Check(content.TranslatePoint(default, w) is { } p && p.Y >= button.TranslatePoint(new Point(0, button.Bounds.Height), w)!.Value.Y - 1, $"{button.Name}: the flyout content sits below the button");
                flyout.Hide(); Settle();
            }
        }
        finally
        {
            Loc.Set(startLanguage);
            w.Close();
            try { Directory.Delete(root, true); File.Delete(sessionPath); } catch { }
        }
        Console.WriteLine($"Gear tests: {passed} passed");
    }
    private static string RepoRoot([CallerFilePath] string path = "") => IOPath.GetFullPath(IOPath.Combine(IOPath.GetDirectoryName(path)!, "..", ".."));
}