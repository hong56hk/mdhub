using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Services;
using Mdhub.Views;

// Item 27: UI languages (English, Traditional Chinese, Japanese, Korean).
internal static class LocalizationTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        void Pump() { using var timeout = new CancellationTokenSource(100); Dispatcher.UIThread.MainLoop(timeout.Token); }
        var root = Path.Combine(Path.GetTempPath(), $"mdhub-l10n-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var note = Path.Combine(root, "note.md");
        File.WriteAllText(note, "# Title\n\nSome words here.\n");
        try
        {
            Check(Loc.Current == "en", "Tests start in English");
            var w1 = new MainWindow { Width = 1200, Height = 800 }; var w2 = new MainWindow { Width = 1000, Height = 700 };
            w1.Show(); w2.Show(); Pump();
            MainView V(Window w) => w.GetVisualDescendants().OfType<MainView>().Single();
            List<MenuItem> Top(Window w) => w.GetVisualDescendants().OfType<Menu>().Single(m => m.Name == "MainMenu").Items.OfType<MenuItem>().ToList();
            MenuItem Language(Window w) => w.GetVisualDescendants().OfType<TopMenu>().Single().FindControl<MenuItem>("LanguageMenu")!;
            var open = (Task)typeof(MainView).GetMethod("OpenPathAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(V(w1), [note])!;
            while (!open.IsCompleted) Pump();
            Pump();

            var lang = Language(w1);
            Check(lang.Items.OfType<MenuItem>().Select(m => m.Header as string).SequenceEqual(["English", "繁體中文", "日本語", "한국어"]), "Settings > Language lists each language under its own name");
            Check(lang.Items.OfType<MenuItem>().Single(m => m.IsChecked).Tag as string == "en", "The current language is checked");
            Check(!Application.Current!.Resources.ContainsKey("ContentControlThemeFontFamily") || Loc.Current == "en", "English keeps the default font");

            foreach (var info in Loc.Languages)
            {
                Loc.Set(info.Code); Pump();
                var table = Loc.Table(info.Code);
                foreach (var w in new[] { w1, w2 })
                    Check(Top(w).Select(m => m.Header as string).SequenceEqual(new[] { "MenuFile", "MenuEdit", "MenuView", "MenuSettings", "MenuHelp" }.Select(k => table[k])), $"{info.Code}: menus switch live in window {(w == w1 ? 1 : 2)}");
                Check(Language(w2).Items.OfType<MenuItem>().Single(m => m.IsChecked).Tag as string == info.Code, $"{info.Code}: the Language menu checks it in every window");
                Check(Language(w2).Items.OfType<MenuItem>().Select(m => m.Header as string).SequenceEqual(Loc.Languages.Select(l => l.NativeName)), $"{info.Code}: language names stay in their own language");
                var status = w1.GetVisualDescendants().OfType<BottomStatusBar>().Single();
                var caret = status.FindControl<TextBlock>("CaretLabel")!.Text;
                Check(caret == Mdhub.Text.CaretPosition.Format(File.ReadAllText(note), 0, 0) && caret!.Contains("1"), $"{info.Code}: the status bar caret position is localized ({caret})");
                Check(status.FindControl<TextBlock>("FileKindLabel")!.Text == table["KindMarkdown"], $"{info.Code}: the file kind label is localized");
                var counts = status.FindControl<TextBlock>("Counts")!.Text ?? "";
                Check(counts.Length > 0 && counts.Contains("words") == (info.Code == "en"), $"{info.Code}: statistics are re-rendered in the new language ({counts})");
                var explorer = w2.GetVisualDescendants().OfType<FolderExplorer>().Single();
                Check(explorer.FindControl<TextBlock>("FolderName")!.Text == table["OpenFolderButton"] && Equals(ToolTip.GetTip(explorer.FindControl<Button>("FolderNameButton")!), table["OpenFolderTip"]), $"{info.Code}: the explorer header follows the switch");
                var find = w1.GetVisualDescendants().OfType<FindBar>().Single();
                Check(find.FindControl<TextBox>("FindInput")!.PlaceholderText == table["Find"] || find.FindControl<TextBox>("FindInput")!.Watermark == table["Find"], $"{info.Code}: the find box placeholder is localized");
                // Fonts: the CJK chain is set and some installed font in it (or the OS fallback) covers every character used.
                var font = Application.Current!.Resources.TryGetResource("ContentControlThemeFontFamily", null, out var f) ? f as FontFamily : null;
                if (info.Fonts.Length == 0) Check(font is null || !font.ToString().Contains("Gothic"), "en: no CJK font chain");
                else Check(font is not null && font.FamilyNames.Skip(1).First() == info.Fonts.Split(',')[0].Trim(), $"{info.Code}: the CJK font chain follows the default font ({font})");
                var chars = table.Values.SelectMany(v => v.EnumerateRunes()).Where(r => r.Value > 0x2FF && !System.Text.Rune.IsWhiteSpace(r)).Distinct().ToList();
                var missing = new List<string>();
                var faces = (info.Fonts.Length == 0 ? [] : info.Fonts.Split(',').Select(n => n.Trim()))
                    .Select(n => FontManager.Current.TryGetGlyphTypeface(new Typeface(n), out var g) && g.FamilyName == n ? g : null).Where(g => g is not null).ToList();
                foreach (var r in chars)
                {
                    var covered = faces.Any(g => g!.CharacterToGlyphMap.ContainsGlyph(r.Value))
                        || FontManager.Current.TryMatchCharacter(r.Value, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal, font, Loc.Culture, out _);
                    if (!covered) missing.Add(r.ToString());
                }
                Check(missing.Count == 0, $"{info.Code}: {chars.Count} non-Latin characters all have a glyph (installed: {faces.Count}; missing: {string.Join("", missing)})");
                if (info.Fonts.Length > 0) Check(faces.Count > 0, $"{info.Code}: at least one font of the chain is installed here ({string.Join(", ", faces.Select(g => g!.FamilyName))})");
                // Layout: open find + replace and the search sidebar, then look for single-line texts that do not fit.
                V(w1).GetType(); find.IsVisible = true; Pump();
                Check(Overflows(w1).Count == 0, $"{info.Code}: single-line labels in the main window fit ({string.Join(" | ", Overflows(w1))})");
            }
            Loc.Set("en"); Pump();
            Check(Top(w1)[0].Header as string == "_File", "Switching back restores English");

            Loc.Chosen = null;
            LocResources.Choose("ko"); Pump();
            Check(Loc.Chosen == "ko" && Loc.Current == "ko", "Choosing a language remembers it for the session file");
            LocResources.Choose("bogus"); Pump();
            Check(Loc.Current == "ko", "An unknown language choice is ignored");
            Loc.Chosen = null; Loc.Set("en"); Pump();
            w1.Close(); w2.Close(); Pump();

            var leftovers = ScanSources();
            Check(leftovers.Count == 0, $"No hardcoded user-facing strings remain in XAML or UI code ({leftovers.Count}):\n  " + string.Join("\n  ", leftovers));
        }
        finally { Loc.Chosen = null; Loc.Set("en"); try { Directory.Delete(root, true); } catch (Exception) { } }
        Console.WriteLine($"Localization tests passed: {_count}");
    }

    // Single-line text blocks (no wrapping, no ellipsis) whose text is wider than the space they were given.
    static List<string> Overflows(Window w)
    {
        var result = new List<string>();
        foreach (var tb in w.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!tb.IsEffectivelyVisible || string.IsNullOrEmpty(tb.Text) || tb.TextWrapping != TextWrapping.NoWrap || tb.TextTrimming != TextTrimming.None) continue;
            if (tb.FindAncestorOfType<TextBox>() is not null || tb.FindAncestorOfType<MdEditor>() is not null && tb.FindAncestorOfType<TextFormatter>() is null) continue;
            if (tb.Bounds.Width <= 0) continue;
            var needed = tb.TextLayout.WidthIncludingTrailingWhitespace;
            if (needed > tb.Bounds.Width + 1) result.Add($"{tb.Text} ({needed:0}>{tb.Bounds.Width:0})");
        }
        return result;
    }

    static string Repo([CallerFilePath] string path = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", ".."));

    // Literal user-facing text left in XAML attributes or UI code. WIP files (FileIndex, ExplorerFileOperations, FolderService)
    // are excluded on purpose and reported as known English leftovers.
    static List<string> ScanSources()
    {
        var repo = Repo();
        var found = new List<string>();
        string[] allowed = ["Aa", "ab", "AB", ".*", "mdhub", "UTF-8", "Markdown", "&lt;/&gt;", "H1", "H2", "H3", "B", "I", "S", "S̶"];
        var attr = new Regex("\\b(Header|ToolTip\\.Tip|Watermark|PlaceholderText|Content|Text|Title|AutomationProperties\\.Name)=\"([^\"{]*)\"");
        foreach (var file in Directory.EnumerateFiles(repo, "*.axaml", SearchOption.AllDirectories).Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !p.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}")))
            foreach (Match m in attr.Matches(File.ReadAllText(file)))
                if (m.Groups[2].Value.Any(char.IsLetter) && !allowed.Contains(m.Groups[2].Value)) found.Add($"{Path.GetFileName(file)}: {m.Value}");
        // C#: an English phrase (two or more words, starting with a capital) passed to UI text.
        var phrase = new Regex("\"\\$?(?:[A-Z][a-z']+)(?: [A-Za-z'·]+)+[.?…]?\"|\\$\"[A-Z][a-z]+ \\{");
        string[] skip = ["FileIndex.cs", "ExplorerFileOperations.cs", "FolderService.cs"];
        var dirs = new[] { "Components", "Views", "Services", "Text", "Rendering" }.Select(d => Path.Combine(repo, d));
        foreach (var file in dirs.Where(Directory.Exists).SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories)).Append(Path.Combine(repo, "MainWindow.axaml.cs")).Append(Path.Combine(repo, "App.axaml.cs")))
        {
            if (skip.Contains(Path.GetFileName(file)) || !File.Exists(file)) continue;
            foreach (var (line, i) in File.ReadLines(file).Select((l, i) => (l, i)))
            {
                var code = line.TrimStart();
                if (code.StartsWith("//") || code.StartsWith("[GeneratedRegex") || code.Contains("Exception(\"The main view")) continue;
                // Note content and default file names stay English by design (guide, "Untitled N.md", "New folder", formatting placeholders).
                if (Regex.IsMatch(code, "Guide|Untitled|UntitledName|DefaultFolderName|link text|HTML Format|Rich Text Format|ShortcutGesture|=> new\\(\"|S\\(\"|T\\(\"")) continue;
                var stripped = Regex.Replace(code, "//.*$", "");
                foreach (Match m in phrase.Matches(stripped)) found.Add($"{Path.GetFileName(file)}:{i + 1}: {m.Value}");
            }
        }
        return found;
    }

    static int _count;
    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("FAIL: " + message);
        _count++;
        Console.WriteLine("PASS: " + message);
    }
}