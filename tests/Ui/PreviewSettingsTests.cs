using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Rendering;
using Mdhub.Services;
using Mdhub.Views;

// Item 35: preview settings gear (fonts, sizes, line height, paragraph spacing), list markers, export CSS.
internal static class PreviewSettingsTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(60); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Settle() { for (var i = 0; i < 8; i++) Pump(); }
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var sessionPath = Path.Combine(Path.GetTempPath(), $"mdhub-previewsettings-{Guid.NewGuid():N}.json");
        SessionManager.PathOverride = sessionPath; SessionManager.Enabled = true;
        PreviewSettings.Import(null);
        var w = new MainWindow { Width = 1300, Height = 900 };
        try
        {
            w.Show(); Pump();
            var view = w.GetVisualDescendants().OfType<MainView>().First();
            var md = w.GetVisualDescendants().OfType<MdEditor>().First();
            const string source = "Some body text with `inline` code.\n\n1. first item\n2. second item\n\n- bullet one\n- bullet two\n\n```\nvar x = 1;\n```\n";
            typeof(MainView).GetMethod("AddDocument", flags)!.Invoke(view, [null, source, "settings.md", null]);
            Settle();
            var content = md.FindControl<StackPanel>("PreviewContent")!;
            TextBlock Paragraph() => content.Children.OfType<TextBlock>().First(t => t.Inlines?.OfType<Run>().Any(r => r.Text?.Contains("body text") == true) == true);
            Run InlineCode() => Paragraph().Inlines!.OfType<Run>().First(r => r.Text == "inline");
            TextBlock CodeBlock() => content.Children.OfType<Border>().Select(b => b.Child).OfType<TextBlock>().First();
            List<(TextBlock Marker, TextBlock Text)> Rows() => content.GetVisualDescendants().OfType<Grid>()
                .Where(g => g.ColumnDefinitions.Count == 2 && g.Children.Count == 2 && g.Children[0] is TextBlock && g.Children[1] is StackPanel)
                .Select(g => ((TextBlock)g.Children[0], ((StackPanel)g.Children[1]).Children.OfType<TextBlock>().First())).ToList();

            // ---- Defaults are the old values ----
            var d = new PreviewSettingsData();
            Check(d.BodyFamily is null && d.BodySize == 15 && d.CodeFamily == "Consolas" && d.CodeSize == 13 && d.LineHeight == 1.6 && d.ParagraphSpacing == 14 && PreviewSettings.IsDefault,
                "Defaults: UI font 15 px, Consolas 13 px, line height 1.6 (24 px), paragraph spacing 14 px");
            Check(Paragraph().FontSize == 15 && Paragraph().LineHeight == 24 && content.Spacing == 14 && CodeBlock().FontSize == 13 && CodeBlock().FontFamily.Name.StartsWith("Consolas"),
                "Default preview renders as before (15/24 body, 14 px spacing, Consolas 13 code)");

            // ---- Gear button ----
            // Item 60 moved the gear from the preview corner into the toolbar (tests/Ui/GearsTests.cs covers placement and visibility).
            var formatter = w.GetVisualDescendants().OfType<TextFormatter>().First();
            var gear = formatter.FindControl<Button>("PreviewSettingsButton")!;
            var icon = formatter.FindControl<Avalonia.Controls.Shapes.Path>("PreviewSettingsIcon")!;
            Check(gear.IsVisible && gear.Flyout is Flyout && gear.Content == icon && icon.StrokeThickness <= 1.5 && icon.Fill is null, "Gear: a thin-stroke icon button that opens a flyout");
            Check(md.FindControl<Button>("PreviewSettingsButton") is null, "No gear is left in the preview corner");
            Check(ToolTip.GetTip(gear) as string == Loc.T("PreviewSettings") && AutomationProperties.GetName(gear) == Loc.T("PreviewSettings"), "Gear has a localized tooltip and name");
            Check(icon.Stroke is ISolidColorBrush stroke && gear.Foreground is ISolidColorBrush fg && stroke.Color == fg.Color, "Gear icon follows the theme's button foreground");
            ThemeService.Apply(AppTheme.Light, persist: false); Settle();
            Check(icon.Stroke is ISolidColorBrush ls && gear.Foreground is ISolidColorBrush lf && ls.Color == lf.Color, "Gear icon follows a live theme switch");
            ThemeService.Apply(AppTheme.Dark, persist: false); Settle();
            var flyout = (Flyout)gear.Flyout!;
            flyout.ShowAt(gear); Settle();
            var panel = flyout.Content as StackPanel;
            Check(panel is not null && new[] { "BodyFontBox", "BodySizeBox", "CodeFontBox", "CodeSizeBox", "LineHeightBox", "ParagraphSpacingBox", "ResetPreviewSettings" }.All(n => panel.GetLogicalDescendants().OfType<Control>().Any(c => c.Name == n)),
                "Flyout has body/code font pickers and sizes, line height, paragraph spacing and Reset");
            T Find<T>(string name) where T : Control => panel!.GetLogicalDescendants().OfType<T>().First(c => c.Name == name);
            var bodySize = Find<NumericUpDown>("BodySizeBox"); var codeSize = Find<NumericUpDown>("CodeSizeBox");
            var lineHeight = Find<NumericUpDown>("LineHeightBox"); var spacing = Find<NumericUpDown>("ParagraphSpacingBox");
            Check(bodySize.Minimum == 10 && bodySize.Maximum == 28 && codeSize.Minimum == 9 && codeSize.Maximum == 24 && lineHeight.Minimum == 1.0m && lineHeight.Maximum == 2.5m && lineHeight.Increment == 0.1m
                && spacing.Minimum == 0 && spacing.Maximum == 32 && spacing.Increment == 2, "Ranges: body 10-28, code 9-24, line height 1.0-2.5 by 0.1, spacing 0-32 by 2");
            var bodyFonts = Find<ComboBox>("BodyFontBox"); var codeFonts = Find<ComboBox>("CodeFontBox");
            Check(bodyFonts.SelectedIndex == 0 && (string)bodyFonts.SelectedItem! == Loc.T("PreviewDefaultFont") && (string)codeFonts.SelectedItem! == "Consolas" && PreviewSettingsFlyout.BodyFonts.Count > 3,
                "Font pickers list installed fonts; defaults selected (UI font, Consolas)");
            Check(PreviewSettingsFlyout.CodeFonts.All(PreviewSettingsFlyout.IsMonospace) || PreviewSettingsFlyout.CodeFonts.Where(f => f != "Consolas").All(PreviewSettingsFlyout.IsMonospace),
                "Code font picker lists monospace fonts only");
            Check(!PreviewSettingsFlyout.IsMonospace("Arial") || !PreviewSettingsFlyout.BodyFonts.Contains("Arial"), "Proportional fonts are not monospace");

            // ---- Live apply through the flyout controls ----
            var refreshes = (int)typeof(MdEditor).GetProperty("SettingsRefreshes", flags)!.GetValue(md)!;
            bodySize.Value = 20; lineHeight.Value = 1.8m; spacing.Value = 20; codeSize.Value = 16;
            var mono = PreviewSettingsFlyout.CodeFonts.FirstOrDefault(f => f != "Consolas");
            if (mono is not null) codeFonts.SelectedItem = mono;
            var serif = PreviewSettingsFlyout.BodyFonts.FirstOrDefault(f => f is "Georgia" or "Times New Roman" or "Cambria") ?? PreviewSettingsFlyout.BodyFonts[0];
            bodyFonts.SelectedItem = serif;
            Settle();
            Check((int)typeof(MdEditor).GetProperty("SettingsRefreshes", flags)!.GetValue(md)! > refreshes, "Each change re-renders the open preview");
            Check(Paragraph().FontSize == 20 && Math.Abs(Paragraph().LineHeight - 36) < 0.01 && content.Spacing == 20 && Paragraph().FontFamily.Name.StartsWith(serif),
                $"Live: body {serif} 20 px, line height 1.8 (36 px), paragraph spacing 20 px");
            Check(CodeBlock().FontSize == 16 && InlineCode().FontSize == 16 && (mono is null || CodeBlock().FontFamily.Name.StartsWith(mono) && InlineCode().FontFamily!.Name.StartsWith(mono))
                && CodeBlock().FontFamily.ToString().Contains("monospace"), $"Live: inline and fenced code use the code font ({mono ?? "Consolas"}, 16 px) with a monospace fallback");

            // ---- Persistence ----
            var saved = SessionStore.Load(sessionPath)?.Preview;
            Check(saved is not null && saved.BodySize == 20 && saved.LineHeight == 1.8 && saved.ParagraphSpacing == 20 && saved.CodeSize == 16 && saved.BodyFamily == serif,
                "Changes are saved in the session file right away");
            var json = SessionStore.Serialize(new SessionData { Preview = saved });
            PreviewSettings.Import(null);
            PreviewSettings.Import(SessionStore.Deserialize(json)!.Preview);
            Check(PreviewSettings.Current == saved, "A saved session restores the settings");
            Check(SessionStore.Deserialize("{\"Version\":1,\"Windows\":[]}")!.Preview is null, "Old session files have no preview settings (defaults)");
            var odd = PreviewSettings.Normalize(new PreviewSettingsData { BodySize = 40, CodeSize = 2, LineHeight = 1.63, ParagraphSpacing = 9.1, CodeFamily = " ", BodyFamily = "  " });
            Check(odd.BodySize == 28 && odd.CodeSize == 9 && odd.LineHeight == 1.6 && odd.ParagraphSpacing == 10 && odd.CodeFamily == "Consolas" && odd.BodyFamily is null,
                "Hand-edited values are clamped and snapped to the steps");

            // ---- Items 24 and 8 still work with custom typography ----
            flyout.Hide(); Pump();
            Settle();
            using (var settle0 = new CancellationTokenSource(700)) Dispatcher.UIThread.MainLoop(settle0.Token);
            typeof(MdEditor).GetMethod("SelectAllPreview", flags)!.Invoke(md, []);
            var copy = (Task<bool>)typeof(MdEditor).GetMethod("CopyPreviewAsync", flags)!.Invoke(md, [])!;
            for (var i = 0; i < 50 && !copy.IsCompleted; i++) Pump();
            var lastCopy = typeof(MdEditor).GetProperty("LastCopy", flags)!.GetValue(md)!;
            var copied = (string)lastCopy.GetType().GetProperty("Plain")!.GetValue(lastCopy)!;
            Check(copy.IsCompleted && copy.Result && copied.Contains("body text") && copied.Contains("second item"), "Item 24: select all and copy still work");
            using (var settle = new CancellationTokenSource(700)) Dispatcher.UIThread.MainLoop(settle.Token);
            var p = Paragraph();
            var target = p.TranslatePoint(new Point(20, p.Bounds.Height / 2), w)!.Value;
            for (var i = 0; i < 2; i++) { w.MouseDown(target, MouseButton.Left); w.MouseUp(target, MouseButton.Left); }
            Pump();
            Check((bool)typeof(MdEditor).GetProperty("IsEditingPreview", flags)!.GetValue(md)!, "Item 8: double-click still edits the block");
            typeof(MdEditor).GetMethod("CancelPreviewEdit", flags | System.Reflection.BindingFlags.Public)!.Invoke(md, []);
            Settle();

            // ---- List markers: baseline / centre alignment from layout bounds, several fonts and sizes ----
            double BaselineIn(TextBlock t) => t.TranslatePoint(new Point(0, t.Padding.Top), content)!.Value.Y + t.TextLayout.TextLines[0].Baseline;
            double FirstLineCentreOfX(TextBlock t)
            {
                var ft = new FormattedText("x", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(t.FontFamily), t.FontSize, Brushes.Black);
                var ink = ft.BuildGeometry(default)!.Bounds;
                return BaselineIn(t) - (ft.Baseline - ink.Center.Y);
            }
            double BulletCentre(TextBlock m)
            {
                var ft = new FormattedText(m.Text!, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(m.FontFamily), m.FontSize, Brushes.Black);
                var ink = ft.BuildGeometry(default)!.Bounds;
                return BaselineIn(m) + (ink.Center.Y - ft.Baseline);
            }
            var fonts = new List<string?> { null, serif };
            if (PreviewSettingsFlyout.BodyFonts.FirstOrDefault(f => f is "Segoe Script" or "Gabriola" or "Impact" or "Comic Sans MS") is { } odd2) fonts.Add(odd2);
            var worstNumber = 0.0; var worstBullet = 0.0; var cases = 0;
            foreach (var font in fonts)
            foreach (var size in new[] { 10.0, 15, 22, 28 })
            foreach (var lh in new[] { 1.0, 1.6, 2.5 })
            {
                PreviewSettings.Update(_ => new PreviewSettingsData { BodyFamily = font, BodySize = size, LineHeight = lh }, persist: false); Settle();
                var rows = Rows();
                if (rows.Count != 4) throw new Exception($"expected 4 list rows, got {rows.Count}");
                foreach (var (marker, text) in rows)
                {
                    var ordered = marker.Text!.EndsWith('.');
                    if (ordered) worstNumber = Math.Max(worstNumber, Math.Abs(BaselineIn(marker) - BaselineIn(text)));
                    else worstBullet = Math.Max(worstBullet, Math.Abs(BulletCentre(marker) - FirstLineCentreOfX(text)));
                    if (Math.Abs(marker.FontSize - (ordered ? size * 1.10 - 1 : size * 1.15)) > 0.001) throw new Exception($"marker size {marker.FontSize} at {size}");
                }
                cases++;
            }
            Console.WriteLine($"  markers: {cases} font/size/line-height cases, worst number baseline offset {worstNumber:F2}px, worst bullet centre offset {worstBullet:F2}px");
            Check(worstNumber <= 0.05, "Ordered-list numbers (1 px under body x 1.10) sit on the first text line's baseline at any font, size and line height");
            Check(worstBullet <= 0.75, "Bullets are centred on the first line (x-height centre) at any font, size and line height");
            Check(true, "Numbers are body x 1.10 - 1 px and bullets 15% bigger than the body text");

            // ---- Export CSS ----
            PreviewSettings.Reset(persist: false);
            var css = MarkdownExporter.Css();
            Check(css.Contains("font-size: 15px; line-height: 24px;") && css.Contains("code { font-family: Consolas,") && css.Contains("margin: 0 0 14px;") && css.Contains("font-size: 13px;"),
                "Export CSS with defaults: 15/24 body, Consolas 13 px code, 14 px paragraph spacing");
            PreviewSettings.Update(_ => new PreviewSettingsData { BodyFamily = "Georgia", BodySize = 18, CodeFamily = "Courier", CodeSize = 12, LineHeight = 2.0, ParagraphSpacing = 24 }, persist: false);
            css = MarkdownExporter.Css();
            Check(css.Contains("font-family: 'Georgia', 'Segoe UI'") && css.Contains("font-size: 18px; line-height: 36px;") && css.Contains("code { font-family: 'Courier', Consolas,")
                && css.Contains("monospace; font-size: 12px;") && css.Contains("margin: 0 0 24px;") && css.Contains("h1 { font-size: 33.6px;"), "Export CSS follows the settings (fonts, sizes, line height, spacing, scaled headings)");
            var html = MarkdownExporter.ToHtml(source, "t", null);
            Check(html.Contains("font-size: 18px; line-height: 36px;"), "HTML export embeds the settings' CSS (PDF uses the same HTML)");

            // ---- Item 56: bullet / ordered list item spacing ----
            PreviewSettings.Reset(persist: false); Settle();
            var dd = new PreviewSettingsData();
            Check(dd.BulletSpacing == 0 && dd.OrderedSpacing == 8 && PreviewSettings.NumberSize == 15.5,
                "Item 56 defaults: bullet spacing 0 px, ordered spacing 8 px, numbers 15.5 px at 15 px body (was 16.5)");
            double Gap(string kind)
            {
                var list = content.Children.OfType<StackPanel>().First(s => PreviewTags.Kind(s) == kind);
                var a = list.Children[0]; var b = list.Children[1];
                return b.TranslatePoint(default, content)!.Value.Y - a.TranslatePoint(new Point(0, a.Bounds.Height), content)!.Value.Y;
            }
            Check(Math.Abs(Gap("ul") - 0) < 0.01 && Math.Abs(Gap("ol") - 8) < 0.01, $"Default list item gaps measured from layout: ul {Gap("ul"):F2}, ol {Gap("ol"):F2}");
            flyout.ShowAt(gear); Settle();
            panel = (StackPanel)flyout.Content!;
            var bulletBox = Find<NumericUpDown>("BulletSpacingBox"); var orderedBox = Find<NumericUpDown>("OrderedSpacingBox");
            Check(bulletBox.Minimum == 0 && bulletBox.Maximum == 24 && bulletBox.Increment == 1 && orderedBox.Minimum == 0 && orderedBox.Maximum == 24 && orderedBox.Increment == 1
                && bulletBox.Value == 0 && orderedBox.Value == 8, "Flyout has bullet and ordered spacing boxes (0-24 by 1, defaults 0 and 8)");
            bulletBox.Value = 3; orderedBox.Value = 17; Settle();
            Check(Math.Abs(Gap("ul") - 3) < 0.01 && Math.Abs(Gap("ol") - 17) < 0.01 && content.Spacing == 14,
                $"Live: list gaps follow the settings independently (ul {Gap("ul"):F2}, ol {Gap("ol"):F2}), paragraph spacing unchanged");
            var saved56 = SessionStore.Load(sessionPath)?.Preview;
            Check(saved56 is { BulletSpacing: 3, OrderedSpacing: 17 }, "List spacings are saved in the session");
            PreviewSettings.Import(null);
            PreviewSettings.Import(SessionStore.Deserialize(SessionStore.Serialize(new SessionData { Preview = saved56 }))!.Preview);
            Check(PreviewSettings.Current.BulletSpacing == 3 && PreviewSettings.Current.OrderedSpacing == 17, "A saved session restores the list spacings");
            var odd56 = PreviewSettings.Normalize(new PreviewSettingsData { BulletSpacing = 30, OrderedSpacing = -4 });
            var snap56 = PreviewSettings.Normalize(new PreviewSettingsData { BulletSpacing = 2.4, OrderedSpacing = 9.6 });
            Check(odd56.BulletSpacing == 24 && odd56.OrderedSpacing == 0 && snap56.BulletSpacing == 2 && snap56.OrderedSpacing == 10, "List spacings are clamped to 0-24 and snapped to 1 px");
            css = MarkdownExporter.Css();
            Check(css.Contains("ul > li + li { margin-top: 3px; } ol > li + li { margin-top: 17px; }") && css.Contains("ol > li::marker { font-size: 15.5px; }"),
                "Export CSS carries both list spacings and the 1 px smaller numbers");
            Find<Button>("ResetPreviewSettings").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
            panel = (StackPanel)flyout.Content!;
            Check(PreviewSettings.IsDefault && Find<NumericUpDown>("BulletSpacingBox").Value == 0 && Find<NumericUpDown>("OrderedSpacingBox").Value == 8
                && Math.Abs(Gap("ul") - 0) < 0.01 && Math.Abs(Gap("ol") - 8) < 0.01 && MarkdownExporter.Css().Contains("ul > li + li { margin-top: 0px; } ol > li + li { margin-top: 8px; }"),
                "Reset restores both list spacings (controls, preview, export)");
            Check(SessionStore.Load(sessionPath)!.Preview is null, "After the list-spacing reset the session stores no preview settings");
            flyout.Hide(); Settle();

            // ---- Reset ----
            PreviewSettings.Update(_ => new PreviewSettingsData { BodySize = 22 });
            flyout.Hide(); flyout.ShowAt(gear); Settle();
            panel = (StackPanel)flyout.Content!;
            Check(Find<NumericUpDown>("BodySizeBox").Value == 22, "Reopening the flyout shows the current values");
            Find<Button>("ResetPreviewSettings").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Settle();
            panel = (StackPanel)flyout.Content!;
            Check(PreviewSettings.IsDefault && Find<NumericUpDown>("BodySizeBox").Value == 15 && Find<NumericUpDown>("LineHeightBox").Value == 1.6m && Paragraph().FontSize == 15 && content.Spacing == 14,
                "Reset to defaults restores every value, the controls and the preview");
            Check(SessionStore.Load(sessionPath)!.Preview is null, "After reset the session stores no preview settings");
            Check(Loc.Languages.All(l => new[] { "PreviewSettings", "PreviewBodyFont", "PreviewCodeFont", "PreviewLineHeight", "PreviewParagraphSpacing", "PreviewResetDefaults", "PreviewDefaultFont", "PreviewBulletSpacing", "PreviewOrderedSpacing" }
                .All(k => Loc.Table(l.Code).TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))), "Every flyout string exists in all 4 languages");
            flyout.Hide();
        }
        finally
        {
            PreviewSettings.Import(null);
            SessionManager.Enabled = false; SessionManager.PathOverride = null;
            w.Close();
            try { File.Delete(sessionPath); } catch (Exception) { }
        }
        Console.WriteLine($"Preview settings tests: {passed} passed");
    }
}