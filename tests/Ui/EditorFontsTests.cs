using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 57: editor gear with exactly two font pickers (body, code); live, saved in the session, separate from the preview.
internal static class EditorFontsTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(60); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Settle() { for (var i = 0; i < 8; i++) Pump(); }
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var sessionPath = Path.Combine(Path.GetTempPath(), $"mdhub-editorfonts-{Guid.NewGuid():N}.json");
        SessionManager.PathOverride = sessionPath; SessionManager.Enabled = true;
        EditorFonts.Import(null); PreviewSettings.Import(null);
        var w = new MainWindow { Width = 1300, Height = 900 };
        try
        {
            w.Show(); Pump();
            var view = w.GetVisualDescendants().OfType<MainView>().First();
            var editor = w.GetVisualDescendants().OfType<TextEditor>().First();
            const string source = "plain words `code span` more\n\n```\nfenced line\n```\n";
            typeof(MainView).GetMethod("AddDocument", flags)!.Invoke(view, [null, "other buffer", "other.txt", null]);
            typeof(MainView).GetMethod("AddDocument", flags)!.Invoke(view, [null, source, "fonts.md", null]);
            Settle();
            var tabs = w.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "Tabs");
            var docs = tabs.Items.OfType<NoteDocument>().ToList();
            var md = docs.Single(d => d.Path is null && d.Text == source);
            var other = docs.Single(d => d.Text == "other buffer");

            // ---- Defaults: the editor's font before item 57 ----
            Check(EditorFonts.Current.Body == "Consolas" && EditorFonts.Current.Code == "Consolas" && EditorFonts.IsDefault, "Defaults: body and code font are Consolas (the current editor font)");
            Check(md.Editor.FontFamily.ToString().Contains("Consolas, Menlo, monospace") && other.Editor.FontFamily == md.Editor.FontFamily, "Editors use the default font with its monospace fallback");

            // ---- Gear ----
            // Item 60 moved the gear from the editor corner into the toolbar (tests/Ui/GearsTests.cs covers placement and visibility).
            var formatter = w.GetVisualDescendants().OfType<Mdhub.Components.TextFormatter>().First();
            var gear = formatter.FindControl<Button>("EditorSettingsButton")!;
            var icon = formatter.FindControl<Avalonia.Controls.Shapes.Path>("EditorSettingsIcon")!;
            Check(gear.IsVisible && gear.Flyout is Flyout && gear.Content == icon && gear.Classes.Contains("tool") && icon.StrokeThickness == 1.25 && icon.Fill is null
                && editor.FindControl<Button>("EditorFontsButton") is null, "Gear: a thin-stroke toolbar icon opening a flyout (no corner gear left)");
            Check(ToolTip.GetTip(gear) as string == Loc.T("EditorSettings") && AutomationProperties.GetName(gear) == Loc.T("EditorSettings"), "Gear has a localized tooltip and name");
            Check(icon.Stroke is ISolidColorBrush s && gear.Foreground is ISolidColorBrush f && s.Color == f.Color, "Gear icon follows the theme's button foreground");

            // ---- Flyout: exactly two dropdowns ----
            var flyout = (Flyout)gear.Flyout!;
            flyout.ShowAt(gear); Settle();
            var panel = ((Control)flyout.Content!).GetLogicalDescendants().OfType<StackPanel>().Single(p => p.Name == "EditorFontsPanel"); // item 62: fonts, then colours
            var inputs = panel.GetLogicalDescendants().OfType<Control>().Where(c => c is ComboBox or NumericUpDown or Button or CheckBox or ToggleButton or TextBox or Slider).ToList();
            var bodyBox = inputs.OfType<ComboBox>().Single(c => c.Name == "EditorBodyFontBox");
            var codeBox = inputs.OfType<ComboBox>().Single(c => c.Name == "EditorCodeFontBox");
            Check(inputs.Count == 2, $"Flyout has exactly two controls, both font dropdowns ({inputs.Count})");
            Check(panel.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).SequenceEqual(new[] { Loc.T("EditorFontSettings"), Loc.T("EditorBodyFont"), Loc.T("EditorCodeFont") }), "Flyout has the title and the Body font / Code font labels");
            Check(panel.GetLogicalDescendants().OfType<TextBlock>().Skip(1).All(t => t.Foreground is ISolidColorBrush b && b.Color == ((ISolidColorBrush)ThemeService.Brush("TextSecondary")).Color), "Labels use the theme's secondary text brush (as the preview flyout)");
            var installed = PreviewSettingsFlyout.BodyFonts;
            var bodyItems = bodyBox.ItemsSource!.Cast<string>().ToList(); var codeItems = codeBox.ItemsSource!.Cast<string>().ToList();
            Check(installed.Count > 3 && installed.All(bodyItems.Contains) && installed.All(codeItems.Contains), $"Both dropdowns list the installed system fonts ({installed.Count})");
            Check((string)bodyBox.SelectedItem! == "Consolas" && (string)codeBox.SelectedItem! == "Consolas", "Both dropdowns default to the current font (Consolas)");

            // ---- Live apply ----
            var body = installed.FirstOrDefault(n => n is "Georgia" or "Arial" or "Segoe UI" or "Calibri") ?? installed.First(n => n != "Consolas");
            var code = PreviewSettingsFlyout.CodeFonts.FirstOrDefault(n => n is "Courier New" or "Cascadia Mono" or "Lucida Console") ?? installed.First(n => n != "Consolas" && n != body);
            bodyBox.SelectedItem = body; Settle();
            Check(md.Editor.FontFamily.Name == body && other.Editor.FontFamily.Name == body && md.Editor.FontFamily.ToString().Contains("monospace"),
                $"Live: body font {body} applies to every open editor (including background tabs)");
            Check(PreviewSettings.IsDefault && PreviewSettings.Current.BodyFamily is null && PreviewSettings.Current.CodeFamily == "Consolas", "Editor fonts leave the preview fonts alone");
            codeBox.SelectedItem = code; Settle();
            var presenter = md.Editor.GetVisualDescendants().OfType<MarkdownTextPresenter>().Single();
            presenter.InvalidateMeasure(); w.UpdateLayout(); Settle();
            string? FamilyAt(string needle)
            {
                var at = source.IndexOf(needle, StringComparison.Ordinal);
                foreach (var line in presenter.TextLayout.TextLines)
                {
                    var pos = line.FirstTextSourceIndex;
                    foreach (var run in line.TextRuns)
                    {
                        if (at >= pos && at < pos + run.Length) return run.Properties?.Typeface.FontFamily.Name;
                        pos += run.Length;
                    }
                }
                return null;
            }
            Check(FamilyAt("code span") == code && FamilyAt("fenced line") == code, $"Live: code spans and fenced code use the code font {code} ({FamilyAt("code span")}, {FamilyAt("fenced line")})");
            Check(FamilyAt("plain words") == body && FamilyAt("more") == body, $"Normal text keeps the body font {body} ({FamilyAt("plain words")})");

            // ---- Session ----
            var saved = SessionStore.Load(sessionPath)?.EditorFonts;
            Check(saved is not null && saved.Body == body && saved.Code == code, "Changes are saved in the session right away");
            EditorFonts.Import(null);
            EditorFonts.Import(SessionStore.Deserialize(SessionStore.Serialize(new SessionData { EditorFonts = saved }))!.EditorFonts);
            Check(EditorFonts.Current == saved, "A saved session restores both fonts");
            Check(SessionStore.Deserialize("{\"Version\":1,\"Windows\":[]}")!.EditorFonts is null, "Old session files have no editor fonts (defaults)");
            var doc3 = typeof(MainView).GetMethod("AddDocument", flags)!.Invoke(view, [null, "third", "third.md", null]); Settle();
            Check(tabs.Items.OfType<NoteDocument>().Single(d => d.Text == "third").Editor.FontFamily.Name == body, "Newly opened editors use the chosen body font");
            flyout.Hide(); flyout.ShowAt(gear); Settle();
            panel = ((Control)flyout.Content!).GetLogicalDescendants().OfType<StackPanel>().Single(p => p.Name == "EditorFontsPanel");
            Check((string)panel.GetLogicalDescendants().OfType<ComboBox>().First().SelectedItem! == body, "Reopening the flyout shows the current fonts");
            panel.GetLogicalDescendants().OfType<ComboBox>().ToList().ForEach(c => c.SelectedItem = "Consolas"); Settle();
            Check(EditorFonts.IsDefault && SessionStore.Load(sessionPath)!.EditorFonts is null && md.Editor.FontFamily.ToString().Contains("Consolas, Menlo, monospace"), "Choosing Consolas again stores no editor fonts in the session");
            flyout.Hide(); Settle();

            Check(Loc.Languages.All(l => new[] { "EditorFontSettings", "EditorBodyFont", "EditorCodeFont" }.All(k => Loc.Table(l.Code).TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))), $"Every string exists in all {Loc.Languages.Count} languages");
            Console.WriteLine($"Editor font tests: {passed} passed");
        }
        finally
        {
            EditorFonts.Import(null);
            SessionManager.Enabled = false; SessionManager.PathOverride = null;
            w.Close();
            try { File.Delete(sessionPath); } catch (Exception) { }
        }
    }
}
