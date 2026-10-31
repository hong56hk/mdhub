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

// Item 74: Hunspell spell checking of Markdown prose (Services/SpellCheck, Components/SpellCheckOverlay).
internal static class SpellCheckTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Settle() { for (var i = 0; i < 8; i++) Pump(); }
        void Until(Func<bool> done) { for (var i = 0; i < 100 && !done(); i++) Pump(); }
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var sessionPath = Path.Combine(Path.GetTempPath(), $"mdhub-spell-{Guid.NewGuid():N}.json");
        var wordsPath = Path.Combine(Path.GetTempPath(), $"mdhub-spell-{Guid.NewGuid():N}", "dictionary.txt");
        SessionManager.PathOverride = sessionPath; SessionManager.Enabled = true;
        SpellCheck.CustomWordsPath = wordsPath; SpellCheck.Reload();
        SpellCheckOverlay.Debounce = TimeSpan.FromMilliseconds(20);
        EditorBehavior.Import(null);
        var uiLanguage = Loc.Current;

        // ---- Engine ----
        Check(!SpellCheck.Enabled && EditorBehavior.Current is { SpellCheck: false, SpellLanguage: null }, "Off by default; the dictionary follows the UI language");
        Check(SpellCheck.Languages.Select(l => l.Code).SequenceEqual(["en_US", "fr", "es_ES"]) && SpellCheck.Languages.All(l => File.Exists(Path.Combine(SpellCheck.DictionaryFolder, l.Code + ".dic")) && File.Exists(Path.Combine(SpellCheck.DictionaryFolder, l.Code + ".aff"))),
            "en_US, fr and es_ES dictionaries are bundled next to the app");
        Check(File.Exists(Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md")), "The third-party notices ship with the app");
        Check(SpellCheck.DefaultFor("fr") == "fr" && SpellCheck.DefaultFor("es") == "es_ES" && SpellCheck.DefaultFor("de") == "en_US" && SpellCheck.DefaultFor("ja") == "en_US" && SpellCheck.DefaultFor("en") == "en_US",
            "Default dictionary: the UI language's, else English");
        Check(SpellCheck.IsCorrect("hello", "en_US") && !SpellCheck.IsCorrect("helo", "en_US") && SpellCheck.IsCorrect("Hello", "en_US") && SpellCheck.IsCorrect("don't", "en_US"), "English: correct and misspelled words");
        Check(SpellCheck.IsCorrect("bonjour", "fr") && SpellCheck.IsCorrect("l’orthographe", "fr") && !SpellCheck.IsCorrect("bonjur", "fr"), "French dictionary works (elision with ’)");
        Check(SpellCheck.IsCorrect("ortografía", "es_ES") && !SpellCheck.IsCorrect("ortografia", "es_ES"), "Spanish dictionary works");
        var sugg = SpellCheck.Suggest("helo", "en_US");
        Check(sugg.Count is > 0 and <= 5 && sugg.Contains("hello"), $"Suggestions: at most 5, the right one included ({string.Join(",", sugg)})");
        Check(SpellCheck.Misspellings("Helo `wrold` https://x.y/zzz NASA abc1 readme.md \u4e2d\u6587 wrold", "en_US").Select(m => m.Word).SequenceEqual(["Helo", "wrold"]), "Misspellings skip code, URLs, ALL-CAPS, digits, file names and CJK");
        SpellCheck.AddWord("mdhub");
        Check(SpellCheck.IsCorrect("mdhub", "en_US") && SpellCheck.IsCorrect("Mdhub", "fr") && File.ReadAllLines(wordsPath).Contains("mdhub"), "Add to dictionary: saved in appdata, for every language");
        SpellCheck.Reload();
        Check(SpellCheck.CustomWords.Contains("mdhub"), "Custom words load back from the file");

        var w = new MainWindow { Width = 1100, Height = 760 };
        try
        {
            w.Show(); Settle();
            var view = w.GetVisualDescendants().OfType<MainView>().Single();
            var editorView = view.GetVisualDescendants().OfType<TextEditor>().Single();
            var status = view.GetVisualDescendants().OfType<BottomStatusBar>().Single();
            var countLabel = status.FindControl<TextBlock>("SpellCountLabel")!;
            NoteDocument Open(string text, string name) { var d = (NoteDocument)typeof(MainView).GetMethod("AddDocument", flags)!.Invoke(view, [null, text, name, null])!; Settle(); d.Editor.Focus(); Settle(); return d; }
            var doc = Open("Helo wrold, this is fine.\n\n```\nbadcode wrold\n```\n\nAnother wrold here.", "spell.md");
            var ed = doc.Editor;
            var overlay = editorView.Spelling;
            Settle();
            Check(overlay.Count is null && !countLabel.IsVisible && overlay.Visible.Count == 0, "Off: no underlines and no count in the status bar");
            var menu = ed.ContextMenu!;
            ed.CaretIndex = 1; ed.RaiseEvent(new ContextRequestedEventArgs()); Settle();
            Check(!menu.Items.OfType<MenuItem>().Any(i => i.Name is "SpellAddMenuItem" or "SpellSuggestionMenuItem"), "Off: the context menu has no spelling items");
            menu.Close(); Settle();
            w.KeyPress(Key.F7, RawInputModifiers.None, PhysicalKey.F7, null); Settle();
            Check(ed.SelectionStart == ed.SelectionEnd, "Off: F7 does nothing");

            // ---- Gear: switch on ----
            var gear = w.GetVisualDescendants().OfType<Button>().First(b => b.Name == "EditorSettingsButton");
            var flyout = (Flyout)gear.Flyout!;
            flyout.ShowAt(gear); Settle();
            var content = (Control)flyout.Content!;
            var box = content.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "EditorSpellCheckBox");
            var lang = content.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "EditorSpellLanguageBox");
            var panels = content.GetVisualDescendants().OfType<StackPanel>().Select(p => p.Name).Where(n => n is "EditorSpellingPanel" or "EditorAutoCompletePanel" or "EditorEncodingPanel").ToList();
            Check(box.IsChecked == false && lang.SelectedIndex == 0 && lang.ItemCount == 4, "Gear: spelling switch off, dictionary 'Match the UI language' plus 3 languages");
            Check(panels.IndexOf("EditorSpellingPanel") >= 0 && (panels.IndexOf("EditorEncodingPanel") < 0 || panels.IndexOf("EditorSpellingPanel") < panels.IndexOf("EditorEncodingPanel")), $"Gear: the spelling section sits before Encoding ({string.Join(",", panels)})");
            box.IsChecked = true; Settle();
            flyout.Hide(); Settle();
            Until(() => overlay.Count is not null && !overlay.IsBusy);
            var T = () => (ed.Text ?? "").Replace("\r\n", "\n");
            Check(overlay.Count == 3 && overlay.All.Select(m => m.Word).SequenceEqual(["Helo", "wrold", "wrold"]), $"On: misspellings found in prose only, not in code ({overlay.Count}: {string.Join(",", overlay.All.Select(m => m.Word))})");
            Check(countLabel.IsVisible && countLabel.Text == Loc.Plural("SpellCountOne", "SpellCountMany", 3), $"Status bar shows the count ({countLabel.Text})");
            Check(overlay.Underlines().Count() == 3 && overlay.Underlines().All(r => r.Width > 0), "Visible misspellings get underlines");

            // ---- Context menu ----
            ed.CaretIndex = 2; ((MarkdownTextBox)ed).ContextIndex = null;
            ed.RaiseEvent(new ContextRequestedEventArgs()); Settle();
            var items = menu.Items.OfType<Control>().ToList();
            var fixes = items.OfType<MenuItem>().Where(i => i.Name == "SpellSuggestionMenuItem").ToList();
            Check(fixes.Count is > 0 and <= 5 && items.IndexOf(fixes[0]) == 0, $"Right-click on a misspelling: up to 5 suggestions at the top ({string.Join(",", items.Select(i => i.Name))} at={overlay.MisspellingAt(2)} sugg={string.Join(",", SpellCheck.Suggest("Helo"))})");
            var add = items.OfType<MenuItem>().Single(i => i.Name == "SpellAddMenuItem");
            var ign = items.OfType<MenuItem>().Single(i => i.Name == "SpellIgnoreMenuItem");
            Check(items.IndexOf(add) == fixes.Count && items.IndexOf(ign) == fixes.Count + 1 && items[fixes.Count + 2] is Separator && add.Header as string == Loc.T("SpellAddToDictionary") && ign.Header as string == Loc.T("SpellIgnoreInFile"),
                "Then 'Add to dictionary', 'Ignore in this file' and a separator");
            Check(items.OfType<MenuItem>().Any(i => i.Name == "EditorUndoMenuItem") && add.Padding == fixes[0].Padding, "The usual items follow, all with the menu padding (Localized)");
            var hello = fixes.FirstOrDefault(f => f.Header as string == "Hello") ?? fixes[0];
            var chosen = (string)hello.Header!;
            hello.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent)); menu.Close(); Settle();
            Check(T().StartsWith(chosen + " wrold"), $"Clicking a suggestion replaces the word ({T()[..12]})");
            ed.Undo(); Settle();
            Check(T().StartsWith("Helo wrold"), "Replacing is one undo step");
            Until(() => overlay.Count == 3 && !overlay.IsBusy);
            ed.CaretIndex = 22; ed.RaiseEvent(new ContextRequestedEventArgs()); Settle();
            Check(!menu.Items.OfType<MenuItem>().Any(i => i.Name is "SpellAddMenuItem" or "SpellSuggestionMenuItem") && menu.Items.OfType<MenuItem>().First().Name == "EditorUndoMenuItem", "On a correct word: no spelling items");
            menu.Close(); Settle();

            // ---- F7 ----
            ed.CaretIndex = 0; Settle();
            ed.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F7 }); Settle();
            string Sel() => ed.Text![ed.SelectionStart..ed.SelectionEnd];
            Check(Sel() == "Helo", $"F7 selects the next misspelling ({Sel()})");
            w.KeyPress(Key.F7, RawInputModifiers.None, PhysicalKey.F7, null); Settle();
            Check(Sel() == "wrold" && ed.SelectionStart == 5, "F7 again goes to the next one");
            w.KeyPress(Key.F7, RawInputModifiers.None, PhysicalKey.F7, null); Settle();
            Check(Sel() == "wrold" && T()[..ed.SelectionStart].Replace("\r\n", "\n").Length > 40, "F7 skips code blocks");
            w.KeyPress(Key.F7, RawInputModifiers.None, PhysicalKey.F7, null); Settle();
            Check(Sel() == "Helo" && ed.SelectionStart == 0, "F7 wraps to the top");
            Check(Shortcuts.All.Any(s => s.Id == "editor.nextMisspelling") && Shortcuts.Get("editor.nextMisspelling").Label == Loc.En["Sc.editor.nextMisspelling"], "F7 is registered in Shortcuts");

            // ---- Ignore / add ----
            ed.CaretIndex = 7; ed.RaiseEvent(new ContextRequestedEventArgs()); Settle();
            menu.Items.OfType<MenuItem>().Single(i => i.Name == "SpellIgnoreMenuItem").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent)); menu.Close(); Settle();
            Until(() => overlay.Count == 1 && !overlay.IsBusy);
            Check(overlay.Count == 1 && overlay.All[0].Word == "Helo" && countLabel.Text == Loc.Plural("SpellCountOne", "SpellCountMany", 1), "Ignore in this file hides every occurrence in this file; the count updates");
            var other = Open("wrold Helo", "other.md");
            Until(() => editorView.Spelling.Count == 2 && !editorView.Spelling.IsBusy);
            Check(editorView.Spelling.Count == 2, "Ignored words still count in other files");
            other.SavedText = other.Text;
            w.GetVisualDescendants().OfType<FileTabs>().Single().FindControl<ListBox>("Tabs")!.SelectedItem = doc; Settle(); doc.Editor.Focus(); Settle();
            Until(() => overlay.Count == 1 && !overlay.IsBusy);
            ed = doc.Editor; ed.CaretIndex = 1; ed.RaiseEvent(new ContextRequestedEventArgs()); Settle();
            menu.Items.OfType<MenuItem>().Single(i => i.Name == "SpellAddMenuItem").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent)); menu.Close(); Settle();
            Until(() => overlay.Count == 0 && !overlay.IsBusy);
            Check(overlay.Count == 0 && File.ReadAllLines(wordsPath).Contains("Helo") && countLabel.Text == Loc.Plural("SpellCountOne", "SpellCountMany", 0), "Add to dictionary: gone everywhere and saved");

            // ---- Typing is debounced and re-checked ----
            ed.CaretIndex = ed.Text!.Length; ed.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = " anothr" }); Settle();
            Until(() => overlay.Count == 1 && !overlay.IsBusy);
            Check(overlay.Count == 1 && overlay.All[0].Word == "anothr", "Typing re-checks in the background");

            // ---- Non-Markdown, language, persistence ----
            var txt = Open("wrold helo", "plain.txt");
            Settle(); Settle();
            Check(editorView.Spelling.Count is null && !countLabel.IsVisible, "Not checked in non-Markdown files");
            txt.SavedText = txt.Text;
            EditorBehavior.Update(d => d with { SpellLanguage = "fr" }); Settle();
            Check(SpellCheck.ActiveLanguage == "fr", "Picking a dictionary overrides the UI language");
            var saved = SessionStore.Load(sessionPath)?.EditorBehavior;
            Check(saved is { SpellCheck: true, SpellLanguage: "fr" }, "The switch and dictionary are saved in the session");
            EditorBehavior.Import(null); EditorBehavior.Import(saved);
            Check(EditorBehavior.Current is { SpellCheck: true, SpellLanguage: "fr" }, "Loading the session restores them");
            EditorBehavior.Import(saved with { SpellLanguage = "de_DE" });
            Check(EditorBehavior.Current.SpellLanguage is null, "An unknown dictionary falls back to the default");
            EditorBehavior.Update(_ => new EditorBehaviorData()); Settle();
            Check(SessionStore.Load(sessionPath)?.EditorBehavior is null, "All defaults are not written");

            // ---- Localization ----
            var keys = new[] { "EditorSpelling", "EditorSpellCheck", "EditorSpellLanguage", "EditorSpellLanguageAuto", "SpellAddToDictionary", "SpellIgnoreInFile", "SpellNoSuggestions", "SpellCountOne", "SpellCountMany", "Sc.editor.nextMisspelling" };
            Check(Loc.Languages.Where(l => l.Code != Loc.English).All(l => keys.All(k => Loc.Table(l.Code).TryGetValue(k, out var v) && v != Loc.En[k])), "Spelling strings are translated in all 8 languages");
            foreach (var d in w.GetVisualDescendants().OfType<FileTabs>().Single().FindControl<ListBox>("Tabs")!.Items.OfType<NoteDocument>()) d.SavedText = d.Text;
        }
        finally
        {
            EditorBehavior.Import(null);
            w.Close();
            try { File.Delete(sessionPath); Directory.Delete(Path.GetDirectoryName(wordsPath)!, true); } catch { }
        }
        Console.WriteLine($"Spell check tests: {passed} passed");
    }
}
