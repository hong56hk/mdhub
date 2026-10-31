using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 62: the editor gear's Colors section: per-theme overrides of theme resource keys, live, saved, resettable.
internal static class EditorColorsTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Settle() { for (var i = 0; i < 8; i++) Pump(); }
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var sessionPath = Path.Combine(Path.GetTempPath(), $"mdhub-editorcolors-{Guid.NewGuid():N}.json");
        SessionManager.PathOverride = sessionPath; SessionManager.Enabled = true;
        EditorFonts.Import(null); PreviewSettings.Import(null); EditorColors.Import(null);
        ThemeService.Apply(AppTheme.Dark, persist: false);
        var app = Application.Current!;
        var w = new MainWindow { Width = 1300, Height = 900 };
        try
        {
            w.Show(); Pump();
            var view = w.GetVisualDescendants().OfType<MainView>().Single();
            var bar = w.GetVisualDescendants().OfType<Mdhub.Components.TextFormatter>().Single();
            var editorPane = w.GetVisualDescendants().OfType<TextEditor>().Single();
            const string source = "# Title\n\n- bullet\n1. number\n\nSome **bold** *ital* ~~gone~~ `code` [link](u)\n\n```\nfenced\n```\n";
            var md = (NoteDocument)typeof(MainView).GetMethod("AddDocument", flags)!.Invoke(view, [null, source, "colors.md", null])!;
            Settle();
            var presenter = md.Editor.GetVisualDescendants().OfType<MarkdownTextPresenter>().Single();
            Color? Ink(string needle)
            {
                presenter.InvalidateMeasure(); w.UpdateLayout(); Pump();
                var at = source.IndexOf(needle, StringComparison.Ordinal);
                foreach (var line in presenter.TextLayout.TextLines)
                {
                    var pos = line.FirstTextSourceIndex;
                    foreach (var run in line.TextRuns)
                    {
                        if (at >= pos && at < pos + run.Length) return (run.Properties?.ForegroundBrush as ISolidColorBrush)?.Color;
                        pos += run.Length;
                    }
                }
                return null;
            }
            Color Res(string key, AppTheme theme) => app.TryGetResource(key, ThemeService.Variant(theme), out var v) && v is ISolidColorBrush b ? b.Color : default;
            Color Pane() => ((ISolidColorBrush)((Grid)editorPane.Content!).Background!).Color;
            var themes = new[] { AppTheme.Dark, AppTheme.Light };

            // ---- Defaults equal the theme values ----
            Check(EditorColors.Items.Count == 13 && EditorColors.IsDefault, "13 colours, no overrides by default");
            Check(themes.All(t => EditorColors.Items.All(i => EditorColors.Get(i.Id, t) == ThemeService.ThemeDefault(i.Keys[0], t) && EditorColors.Get(i.Id, t) == Res(i.Keys[0], t) && EditorColors.Get(i.Id, t).A > 0)),
                "Each colour defaults to its theme resource, in Dark and Light");
            Check(themes.All(t => ThemeService.ThemeDefault("EditorTextBackground", t) == ThemeService.ThemeDefault("EditorBackground", t) && ThemeService.ThemeDefault("SyntaxOrderedMarker", t) == ThemeService.ThemeDefault("SyntaxListMarker", t)),
                "New keys keep the old look: editor background = EditorBackground, list numbers = the list marker colour");
            Check(((ISolidColorBrush)md.Editor.Foreground!).Color == Res("EditorForeground", AppTheme.Dark) && ((ISolidColorBrush)md.Editor.SelectionBrush!).Color == Res("EditorSelectionBackground", AppTheme.Dark)
                && Pane() == Res("EditorTextBackground", AppTheme.Dark), "Editor text, selection and background use the new keys");
            Check(Ink("Title") == Res("SyntaxH1", AppTheme.Dark) && Ink("- bullet") == Res("SyntaxListMarker", AppTheme.Dark) && Ink("1. number") == Res("SyntaxOrderedMarker", AppTheme.Dark) && Ink("**bold") == Res("SyntaxMarker", AppTheme.Dark),
                "Default editor colours render as before");

            // ---- Flyout: Colors section ----
            var gear = bar.FindControl<Button>("EditorSettingsButton")!;
            void Click(Button b) { typeof(Button).GetMethod("OnClick", flags)!.Invoke(b, null); Settle(); }
            var flyout = (Flyout)gear.Flyout!;
            Click(gear);
            var root = (Control)flyout.Content!;
            StackPanel Colors() => ((Control)flyout.Content!).GetLogicalDescendants().OfType<StackPanel>().Single(p => p.Name == "EditorColorsPanel");
            T Find<T>(string name) where T : Control => Colors().GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);
            Check(root is ScrollViewer { MaxHeight: > 300 and <= 600 } sv && sv.VerticalScrollBarVisibility == Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                && root.GetLogicalDescendants().OfType<StackPanel>().Any(p => p.Name == "EditorFontsPanel"), "Flyout scrolls (max height) and keeps the fonts above the colours");
            Check(Colors().Children.OfType<TextBlock>().First().Text == Loc.T("EditorColors") && EditorColors.Items.All(i => Colors().GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == Loc.T(i.Label))),
                "Colors title and a localized label per colour");
            Check(EditorColors.Items.All(i => Find<Button>("EditorColor" + i.Id).Flyout is Flyout && AutomationProperties.GetName(Find<Button>("EditorColor" + i.Id)) == Loc.T(i.Label)
                && ((ISolidColorBrush)((Border)Find<Button>("EditorColor" + i.Id).Content!).Background!).Color == EditorColors.Get(i.Id) && !Find<Button>("EditorColorReset" + i.Id).IsEnabled)
                && !Find<Button>("ResetEditorColors").IsEnabled && (string)Find<Button>("ResetEditorColors").Content! == Loc.T("ResetEditorColors"),
                "Each colour: a swatch picker showing the current colour and a (disabled) reset; 'Reset colors' disabled");
            var rowHeight = Find<Button>("EditorColorBackground").Bounds.Height;
            Check(rowHeight > 0 && rowHeight <= 26 && Colors().Bounds.Width <= 280, $"Compact rows ({rowHeight:F0} px high, panel {Colors().Bounds.Width:F0} px wide)");

            // ---- Live override through the picker ----
            var swatch = Find<Button>("EditorColorHeadings");
            var picker = (Flyout)swatch.Flyout!;
            Click(swatch);
            var pick = (StackPanel)picker.Content!;
            var hex = pick.GetLogicalDescendants().OfType<TextBox>().Single(t => t.Name == "EditorColorHex");
            Check(hex.Text == $"#{EditorColors.Default("Headings").R:X2}{EditorColors.Default("Headings").G:X2}{EditorColors.Default("Headings").B:X2}" && pick.GetLogicalDescendants().OfType<Slider>().Count() == 3,
                "Picker: hex box with the current colour and red/green/blue sliders");
            var headings = Color.FromRgb(0x12, 0x34, 0x56);
            hex.Text = "#123456"; Settle();
            Check(EditorColors.Get("Headings") == headings && new[] { "SyntaxH1", "SyntaxH2", "SyntaxH3", "SyntaxH4" }.All(k => Res(k, AppTheme.Dark) == headings && ThemeService.Brush(k).Color == headings),
                "Live: the hex box overrides every heading key (Application resources over the theme)");
            Check(Ink("Title") == headings, "Live: the editor repaints headings in the new colour");
            Check(ThemeService.ThemeDefault("SyntaxH1", AppTheme.Dark) != headings && Res("SyntaxH1", AppTheme.Light) == ThemeService.ThemeDefault("SyntaxH1", AppTheme.Light), "The palette itself and the Light theme are untouched");
            Check(Find<Button>("EditorColorResetHeadings").IsEnabled && Find<Button>("ResetEditorColors").IsEnabled
                && ((ISolidColorBrush)((Border)swatch.Content!).Background!).Color == headings, "The swatch shows the new colour; its reset and 'Reset colors' turn on");
            var red = pick.GetLogicalDescendants().OfType<Slider>().Single(s => s.Name == "EditorColorSliderRed");
            red.Value = 200; Settle();
            Check(EditorColors.Get("Headings") == Color.FromRgb(200, 0x34, 0x56) && hex.Text == "#C83456", "Live: the red slider changes the colour and the hex box");
            picker.Hide(); Settle();
            var numbers = Color.FromRgb(0xAA, 0x10, 0x10);
            EditorColors.Set("OrderedNumbers", numbers); Settle();
            Check(Ink("1. number") == numbers && Ink("- bullet") == ThemeService.ThemeDefault("SyntaxListMarker", AppTheme.Dark), "List numbers and bullet markers are separate colours");
            var bg = Color.FromRgb(0x05, 0x06, 0x07); var fg = Color.FromRgb(0xEE, 0xDD, 0xCC); var sel = Color.FromArgb(0x80, 0x20, 0x80, 0x20);
            EditorColors.Set("Background", bg); EditorColors.Set("Text", fg); EditorColors.Set("Selection", sel); Settle();
            Check(Pane() == bg && ((ISolidColorBrush)md.Editor.Foreground!).Color == fg && ((ISolidColorBrush)md.Editor.SelectionBrush!).Color == sel,
                "Live: background, body text and selection");
            Check(themes.All(t => ThemeService.ThemeDefault("EditorBackground", t) == Res("EditorBackground", t)), "Background only overrides the editor's own key (image/binary views keep EditorBackground)");
            foreach (var (id, needle) in new[] { ("Links", "link]"), ("Bold", "bold**"), ("Italic", "ital*"), ("Strikethrough", "gone~"), ("InlineCode", "code`"), ("CodeBlocks", "fenced"), ("Markers", "**bold") })
            {
                var c = Color.FromRgb(0x40, (byte)(id.Length * 9), 0x99);
                EditorColors.Set(id, c); Settle();
                Check(Ink(needle) == c, $"Live: {id} ({needle})");
            }

            // ---- Per theme ----
            var darkLinks = EditorColors.Get("Links");
            ThemeService.Apply(AppTheme.Light, persist: false); Settle();
            Check(EditorColors.Items.All(i => EditorColors.Get(i.Id) == ThemeService.ThemeDefault(i.Keys[0], AppTheme.Light)) && Ink("Title") == ThemeService.ThemeDefault("SyntaxH1", AppTheme.Light)
                && Pane() == ThemeService.ThemeDefault("EditorTextBackground", AppTheme.Light), "Switching to Light shows Light's own (default) colours");
            var lightLinks = Color.FromRgb(0x01, 0x77, 0x02);
            EditorColors.Set("Links", lightLinks); Settle();
            Check(Ink("link]") == lightLinks && EditorColors.Get("Links", AppTheme.Dark) == darkLinks, "A Light override leaves Dark's alone");
            ThemeService.Apply(AppTheme.Dark, persist: false); Settle();
            Check(Ink("link]") == darkLinks && Ink("Title") == Color.FromRgb(200, 0x34, 0x56) && Pane() == bg, "Back in Dark: Dark's overrides return");

            // ---- Persistence ----
            var saved = SessionStore.Load(sessionPath)?.EditorColors;
            Check(saved?.Dark is { Count: 12 } && !saved.Dark.ContainsKey("Bullets") && saved.Light is { Count: 1 } && Color.Parse(saved.Light["Links"]) == lightLinks && Color.Parse(saved.Dark["Headings"]) == Color.FromRgb(200, 0x34, 0x56),
                "Saved in the session per theme right away");
            EditorColors.Import(null); Settle();
            Check(EditorColors.IsDefault && Ink("Title") == ThemeService.ThemeDefault("SyntaxH1", AppTheme.Dark), "Clearing restores the theme colours");
            EditorColors.Import(SessionStore.Deserialize(SessionStore.Serialize(new SessionData { EditorColors = saved }))!.EditorColors); Settle();
            Check(EditorColors.Get("Links", AppTheme.Light) == lightLinks && EditorColors.Get("Headings") == Color.FromRgb(200, 0x34, 0x56) && Ink("link]") == darkLinks, "A saved session restores both themes' colours");
            Check(SessionStore.Deserialize("{\"Version\":1,\"Windows\":[]}") is { EditorColors: null }, "Old session files load with no editor colours (defaults)");
            EditorColors.Import(new EditorColorsData { Dark = new() { ["Nope"] = "#FF0000", ["Links"] = "not a colour" } });
            Check(EditorColors.IsDefault, "Unknown ids and bad values in a session are ignored");
            EditorColors.Import(saved); Settle();

            // ---- Per-colour reset and Reset colors ----
            flyout.Hide(); Settle(); Click(gear);
            Click(Find<Button>("EditorColorResetLinks"));
            Check(!EditorColors.IsOverridden("Links") && Ink("link]") == ThemeService.ThemeDefault("SyntaxLink", AppTheme.Dark) && EditorColors.IsOverridden("Headings") && EditorColors.Get("Links", AppTheme.Light) == lightLinks
                && !Find<Button>("EditorColorResetLinks").IsEnabled && ((ISolidColorBrush)((Border)Find<Button>("EditorColorLinks").Content!).Background!).Color == EditorColors.Default("Links"),
                "Per-colour reset: only that colour (in this theme) returns to the theme value");
            Check(SessionStore.Load(sessionPath)?.EditorColors?.Dark?.ContainsKey("Links") == false, "The reset is saved");
            Click(Find<Button>("ResetEditorColors"));
            Check(!EditorColors.HasOverrides(AppTheme.Dark) && EditorColors.Items.All(i => !Find<Button>("EditorColorReset" + i.Id).IsEnabled) && !Find<Button>("ResetEditorColors").IsEnabled
                && Ink("Title") == ThemeService.ThemeDefault("SyntaxH1", AppTheme.Dark) && Pane() == ThemeService.ThemeDefault("EditorTextBackground", AppTheme.Dark), "Reset colors: every Dark colour returns to the theme");
            Check(EditorColors.Get("Links", AppTheme.Light) == lightLinks && SessionStore.Load(sessionPath)?.EditorColors is { Dark: null, Light.Count: 1 }, "Reset colors resets the current theme only (Light keeps its override)");
            EditorColors.ResetAll(AppTheme.Light); Settle();
            Check(EditorColors.IsDefault && SessionStore.Load(sessionPath)?.EditorColors is null, "With no overrides the session stores no editor colours");
            EditorColors.Set("Bold", EditorColors.Default("Bold")); 
            Check(EditorColors.IsDefault, "Picking the theme's own colour stores no override");
            flyout.Hide(); Settle();

            // ---- Strings ----
            var keys = new[] { "EditorColors", "EditorColorReset", "ResetEditorColors", "EditorColorHex", "EditorColorRed", "EditorColorGreen", "EditorColorBlue" }.Concat(EditorColors.Items.Select(i => i.Label)).ToList();
            Check(Loc.Languages.All(l => keys.All(k => Loc.Table(l.Code).TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))), $"Every string exists in all {Loc.Languages.Count} languages");
        }
        finally
        {
            EditorColors.Import(null);
            w.Close();
            try { File.Delete(sessionPath); } catch { }
        }
        Console.WriteLine($"Editor colour tests: {passed} passed");
    }
}