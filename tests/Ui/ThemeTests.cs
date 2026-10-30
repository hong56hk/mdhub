using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 23: Light/Dark theme setting, live switching, persistence and a hardcoded-colour scan.
internal static class ThemeTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        void Pump() { using var timeout = new CancellationTokenSource(100); Dispatcher.UIThread.MainLoop(timeout.Token); }
        var app = Application.Current!;
        var repo = RepoRoot();
        var themeFile = File.ReadAllText(Path.Combine(repo, "Styles", "Theme.axaml"));
        var dicts = Regex.Matches(themeFile, @"<ResourceDictionary x:Key=""(\w+)"">(.*?)</ResourceDictionary>", RegexOptions.Singleline)
            .ToDictionary(m => m.Groups[1].Value, m => Regex.Matches(m.Groups[2].Value, @"x:Key=""(\w+)""").Select(k => k.Groups[1].Value).ToList());
        var keys = dicts["Dark"];

        Check(ThemeService.Current == AppTheme.Dark && app.RequestedThemeVariant == ThemeVariant.Dark, "default theme is Dark");
        Check(dicts.Count == 2 && keys.Count > 40 && keys.ToHashSet().SetEquals(dicts["Light"]) && keys.Distinct().Count() == keys.Count, $"Dark and Light define the same {keys.Count} keys");
        Check(keys.All(k => ThemeService.Lookup(k, AppTheme.Dark).A > 0 && ThemeService.Lookup(k, AppTheme.Light).A > 0), "every key resolves in both themes");
        Check(ThemeService.Lookup("AppBackground", AppTheme.Dark) == Color.Parse("#17191D") && ThemeService.Lookup("EditorBackground", AppTheme.Dark) == Color.Parse("#202329")
            && ThemeService.Lookup("PreviewBackground", AppTheme.Dark) == Color.Parse("#101318") && ThemeService.Lookup("CurrentLineBackground", AppTheme.Dark) == Color.FromArgb(32, 211, 211, 211),
            "Dark keeps the original colours");

        // Legibility: WCAG contrast of foregrounds against the surfaces they sit on, in both themes.
        var pairs = new (string Fg, string Bg, double Min)[] {
            ("TextPrimary", "EditorBackground", 7), ("TextPrimary", "AppBackground", 7), ("TextStrong", "AppBackground", 7), ("FolderNameForeground", "AppBackground", 4.5),
            ("TextSecondary", "AppBackground", 4.5), ("TextMuted", "AppBackground", 4.5), ("SidebarTabForeground", "AppBackground", 4.5), ("SectionForeground", "AppBackground", 4.5),
            ("IconForeground", "AppBackground", 4.5), ("IconForeground", "FloatingBackground", 4.5), ("IconForeground", "HoverBackground", 3), ("AcceleratorForeground", "AppBackground", 3),
            ("Accent", "AppBackground", 3), ("Accent", "TabStripBackground", 3), ("GutterForeground", "EditorBackground", 3), ("SyntaxH1", "EditorBackground", 4.5), ("SyntaxH2", "EditorBackground", 4.5),
            ("SyntaxH3", "EditorBackground", 4.5), ("SyntaxH4", "EditorBackground", 4.5), ("SyntaxLink", "EditorBackground", 4.5), ("SyntaxListMarker", "EditorBackground", 4.5),
            ("SyntaxQuote", "EditorBackground", 4.5), ("SyntaxCheckbox", "EditorBackground", 4.5), ("SyntaxInlineCode", "SyntaxInlineCodeBackground", 4.5),
            ("SyntaxMarker", "EditorBackground", 2.5), ("SyntaxUrl", "EditorBackground", 3), ("SyntaxChecked", "EditorBackground", 3),
            ("SyntaxCodeText", "SyntaxCodeBlockBackground", 7), ("SyntaxCodeKeyword", "SyntaxCodeBlockBackground", 4.5), ("SyntaxCodeString", "SyntaxCodeBlockBackground", 4.5),
            ("SyntaxCodeComment", "SyntaxCodeBlockBackground", 4.5), ("SyntaxCodeNumber", "SyntaxCodeBlockBackground", 4.5), ("SyntaxCodeType", "SyntaxCodeBlockBackground", 4.5),
            ("SyntaxCodeProperty", "SyntaxCodeBlockBackground", 4.5), ("SyntaxCodeTag", "SyntaxCodeBlockBackground", 4.5), ("SyntaxCodeAttribute", "SyntaxCodeBlockBackground", 4.5), ("LinkForeground", "PreviewBackground", 4.5),
            ("InlineCodeForeground", "InlineCodeBackground", 4.5), ("ErrorForeground", "FloatingBackground", 3), ("DialogErrorForeground", "AppBackground", 3),
            ("SearchMatchForeground", "SearchMatchBackground", 4.5), ("ShortcutKeyForeground", "AppBackground", 4.5), ("DialogHintForeground", "AppBackground", 4.5),
            ("PreviewPlaceholderForeground", "PreviewBackground", 3) };
        foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
        {
            var weak = pairs.Select(p => (p, ratio: Contrast(ThemeService.Lookup(p.Fg, theme), ThemeService.Lookup(p.Bg, theme)))).Where(x => x.ratio < x.p.Min).ToList();
            Check(weak.Count == 0, $"{theme}: text and icons meet contrast minimums ({pairs.Length} pairs){string.Concat(weak.Select(w => $" {w.p.Fg}/{w.p.Bg}={w.ratio:0.0}"))}");
            var surfaces = new[] { "EditorBackground", "AppBackground", "PreviewBackground", "FloatingBackground" };
            Check(surfaces.All(s => Contrast(ThemeService.Lookup("TextPrimary", theme), ThemeService.Lookup(s, theme)) >= 7) && (theme == AppTheme.Light) == (Luminance(ThemeService.Lookup("EditorBackground", theme)) > 0.5),
                $"{theme}: surfaces are {(theme == AppTheme.Light ? "light" : "dark")} with readable text");
        }

        // Two windows with a Markdown document that has a code block, table, link and inline code; find bar open.
        var windows = new[] { new MainWindow { Width = 1300, Height = 800 }, new MainWindow { Width = 1100, Height = 700 } };
        foreach (var w in windows) w.Show();
        Pump();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var md = "# Title\n\n- item\n1. one\n\nSome `code` and a [link](https://example.com).\n\n```\nblock\n```\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n---\nfind find\n";
        foreach (var w in windows)
        {
            var view = w.GetVisualDescendants().OfType<MainView>().First();
            typeof(MainView).GetMethod("AddDocument", flags)!.Invoke(view, [null, md, "theme.md", null]);
            Pump(); Pump();
            w.GetVisualDescendants().OfType<TextEditor>().First().OpenFind(false); Pump();
            w.GetVisualDescendants().OfType<FindBar>().First().GetVisualDescendants().OfType<TextBox>().First(b => b.Classes.Contains("findField")).Text = "find";
            Pump(); Pump();
        }
        Color? C(IBrush? b) => (b as ISolidColorBrush)?.Color;
        T Named<T>(Window w, string name) where T : Control => w.GetVisualDescendants().OfType<T>().Concat(Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(w).OfType<T>()).First(c => c.Name == name);
        bool Painted(Window w, AppTheme theme)
        {
            Color L(string key) => ThemeService.Lookup(key, theme);
            var menu = w.GetVisualDescendants().OfType<TopMenu>().First();
            var preview = Named<StackPanel>(w, "PreviewContent");
            if (Environment.GetEnvironmentVariable("MDHUB_THEME_DEBUG") == "1") Console.WriteLine($"  preview children={preview.Children.Count} visible={preview.IsEffectivelyVisible}");
            var previewBorders = preview.GetVisualDescendants().OfType<Border>().ToList();
            var status = w.GetVisualDescendants().OfType<BottomStatusBar>().First();
            var findBar = w.GetVisualDescendants().OfType<FindBar>().First();
            var checks = new Dictionary<string, bool>
            {
                ["window"] = C(w.Background) == L("AppBackground"),
                ["variant"] = w.ActualThemeVariant == ThemeService.Variant(theme),
                ["explorer"] = C(Named<Grid>(w, "FolderPanel").Background) == L("AppBackground"),
                ["tabs"] = C(w.GetVisualDescendants().OfType<FileTabs>().First().GetVisualDescendants().OfType<Grid>().First().Background) == L("TabStripBackground"),
                ["editor"] = C(w.GetVisualDescendants().OfType<TextEditor>().First().GetVisualDescendants().OfType<Grid>().First().Background) == L("EditorBackground"),
                ["preview"] = C(Named<Grid>(w, "PreviewPanel").Background) == L("PreviewBackground"),
                ["codeBlock"] = previewBorders.Any(b => C(b.Background) == L("CodeBlockBackground")),
                ["tableHeader"] = previewBorders.Any(b => C(b.Background) == L("TableHeaderBackground")) && previewBorders.Any(b => C(b.BorderBrush) == L("TableBorder")),
                ["inline"] = preview.GetVisualDescendants().OfType<TextBlock>().SelectMany(t => t.Inlines ?? []).Any(i => C(i.Background) == L("InlineCodeBackground"))
                    && preview.GetVisualDescendants().OfType<TextBlock>().SelectMany(t => t.Inlines ?? []).Any(i => C(i.Foreground) == L("LinkForeground")),
                ["findBar"] = findBar.GetVisualDescendants().OfType<Border>().Any(b => C(b.Background) == L("FloatingBackground") && C(b.BorderBrush) == L("PanelBorder")),
                ["status"] = C(status.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "EncodingLabel").Foreground) == L("Accent"),
                ["menu"] = C(Named<Menu>(w, "MainMenu").Background) == L("AppBackground")
                    && Named<MenuItem>(w, theme == AppTheme.Dark ? "ThemeDarkMenuItem" : "ThemeLightMenuItem").IsChecked
                    && !Named<MenuItem>(w, theme == AppTheme.Dark ? "ThemeLightMenuItem" : "ThemeDarkMenuItem").IsChecked,
                ["gutter"] = C(Brush<LineNumberGutter>("NumberBrush")) == L("GutterForeground"),
                ["currentLine"] = C(CurrentLineOverlay.LineBrush) == L("CurrentLineBackground"),
                ["findHighlights"] = C(Brush<FindHighlightOverlay>("MatchBrush")) == L("FindMatchBackground") && C(Brush<FindHighlightOverlay>("CurrentBrush")) == L("FindCurrentBackground"),
                ["syntax"] = C(MarkdownTextPresenter.BrushFor(Mdhub.Text.SyntaxColor.H1)!) == L("SyntaxH1") && C(MarkdownBlockOverlay.CodeBrush) == L("SyntaxCodeBlockBackground"),
            };
            var bad = checks.Where(c => !c.Value).Select(c => c.Key).ToList();
            if (bad.Count > 0) Console.WriteLine($"  {theme} mismatches: {string.Join(", ", bad)}");
            return bad.Count == 0;
        }
        Check(windows.All(w => Painted(w, AppTheme.Dark)), "both windows start Dark across explorer, tabs, editor, preview, find bar, status bar, menus, gutter and overlays");
        var lightItem = Named<MenuItem>(windows[0], "ThemeLightMenuItem");
        lightItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump(); Pump();
        Check(ThemeService.Current == AppTheme.Light && app.RequestedThemeVariant == ThemeVariant.Light, "Settings > Theme > Light switches the app theme");
        Check(windows.All(w => Painted(w, AppTheme.Light)), "Light applies live to every open window, including code blocks, tables, gutter and overlays");
        var late = new MainWindow(); late.Show(); Pump();
        Check(C(late.Background) == ThemeService.Lookup("AppBackground", AppTheme.Light) && Named<MenuItem>(late, "ThemeLightMenuItem").IsChecked, "a window opened after the switch is Light");
        late.Close(); Pump();
        var dialog = DialogService.PromptNameAsync(windows[0], "New", Path.GetTempPath(), "x.md"); Pump(); Pump();
        var dw = windows[0].OwnedWindows.FirstOrDefault();
        Check(dw is not null && dw.ActualThemeVariant == ThemeVariant.Light, "dialogs follow the theme");
        dw?.Close(); Pump();
        Named<MenuItem>(windows[1], "ThemeDarkMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump(); Pump();
        Check(ThemeService.Current == AppTheme.Dark && windows.All(w => Painted(w, AppTheme.Dark)), "switching back to Dark restores every window");

        // Persistence in session.json.
        var data = SessionStore.Deserialize(SessionStore.Serialize(new SessionData { Theme = "Light" }));
        Check(data?.Theme == "Light", "session round-trips the theme");
        Check(SessionStore.Deserialize("{\"Version\":1,\"Windows\":[]}")?.Theme == "Dark" && ThemeService.Parse(null) == AppTheme.Dark && ThemeService.Parse("garbage") == AppTheme.Dark,
            "missing or unknown theme defaults to Dark");
        var temp = Path.Combine(Path.GetTempPath(), "mdhub-theme-" + Guid.NewGuid().ToString("N") + ".json");
        SessionManager.PathOverride = temp; SessionManager.Enabled = true;
        try
        {
            ThemeService.Apply(AppTheme.Light); Pump();
            Check(SessionStore.Load(temp)?.Theme == "Light", "switching saves the choice right away");
            ThemeService.Apply(AppTheme.Dark); Pump();
            Check(SessionStore.Load(temp)?.Theme == "Dark", "switching back saves Dark");
        }
        finally { SessionManager.Enabled = false; SessionManager.PathOverride = null; SessionManager.Reset(); if (File.Exists(temp)) File.Delete(temp); }

        // Leftover hardcoded colours in app sources (Theme.axaml is the palette; tests are excluded).
        var colour = new Regex(@"#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?\b|Brush\.Parse|Color\.Parse|Color\.From(?:A)?Rgb|\bBrushes\.(?!Transparent\b)\w+|\bColors\.(?!Transparent\b)\w+");
        var named = new Regex(@"\b(?:Foreground|Background|BorderBrush|Stroke|Fill|Color)=""(?!\{|Transparent"")[A-Za-z]+""");
        var allowed = new[] { "BoxShadow=\"0 2 8 0 #66000000\"" };
        var sources = Directory.EnumerateFiles(repo, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".axaml"))
            .Where(f => !Regex.IsMatch(Path.GetRelativePath(repo, f), @"^(tests|bin|obj|artifacts|\.nuget|\.dotnet-home|test-folder)[\\/]"))
            .Where(f => Path.GetFileName(f) != "Theme.axaml").ToList();
        var leftovers = sources.SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, i, line)))
            .Where(x => colour.IsMatch(allowed.Aggregate(x.line, (l, a) => l.Replace(a, ""))) || x.f.EndsWith(".axaml") && named.IsMatch(x.line))
            .Select(x => $"{Path.GetRelativePath(repo, x.f)}:{x.i + 1}").ToList();
        Check(sources.Count > 30 && leftovers.Count == 0, $"no hardcoded colours left in {sources.Count} source files{(leftovers.Count > 0 ? ": " + string.Join(", ", leftovers) : "")}");
        var used = sources.SelectMany(f => Regex.Matches(File.ReadAllText(f), @"DynamicResource (\w+)\}|ThemeService\.Brush\(""(\w+)""\)").Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)).ToHashSet();
        var fluentKeys = new HashSet<string>();
        // Localized strings are DynamicResources too; they are checked by the localization tests.
        used.RemoveWhere(Mdhub.Localization.Loc.En.ContainsKey);
        var missing = used.Where(k => !keys.Contains(k)).ToList();
        Check(used.Count > 30 && missing.Count == 0, $"all {used.Count} theme keys used in code exist in the palette{(missing.Count > 0 ? ": " + string.Join(", ", missing) : "")}");

        foreach (var w in windows)
        {
            foreach (var d in w.GetVisualDescendants().OfType<ListBox>().First(c => c.Name == "Tabs").Items.OfType<NoteDocument>()) d.SavedText = d.Text;
            w.Close();
        }
        Pump();
    }

    private static IBrush? Brush<T>(string field) => typeof(T).GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null) as IBrush;
    private static double Luminance(Color c)
    {
        double Ch(byte v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }
    // Translucent foregrounds are blended over the background first.
    private static double Contrast(Color fg, Color bg)
    {
        var a = fg.A / 255.0;
        var mixed = Color.FromRgb((byte)(fg.R * a + bg.R * (1 - a)), (byte)(fg.G * a + bg.G * (1 - a)), (byte)(fg.B * a + bg.B * (1 - a)));
        var (l1, l2) = (Luminance(mixed), Luminance(bg));
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }
    private static string RepoRoot([CallerFilePath] string path = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", ".."));
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}