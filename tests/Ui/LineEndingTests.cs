using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Text;
using Mdhub.Views;
using IOPath = System.IO.Path;

// Item 68 (VS Code): line endings: detection and the majority rule, round-trip saves per ending, the status bar
// switch (converts, marks modified, one undo), Enter and paste use the file's ending, the Default line ending
// setting for new files, and the mixed-endings warning that normalizes.
internal static class LineEndingTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(15); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); }
        T Result<T>(Task<T> t) { Wait(t); return t.Result; }
        var root = IOPath.Combine(IOPath.GetTempPath(), $"mdhub-eol-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        string F(string name, string text) { var p = IOPath.Combine(root, name); File.WriteAllBytes(p, Encoding.UTF8.GetBytes(text)); return p; }
        var sessionPath = IOPath.Combine(IOPath.GetTempPath(), $"mdhub-eol-{Guid.NewGuid():N}.json");
        SessionManager.PathOverride = sessionPath;
        var startLanguage = Loc.Info.Code;
        var w = new MainWindow { Width = 1300, Height = 700 };
        try
        {
            Loc.Set(Loc.English);
            // --- Detection and the majority rule (pure) ---
            Check(LineEndings.Count("a\r\nb\nc\rd\r\n") == new LineEndingCounts(2, 1, 1), "Count: CRLF, LF and CR are told apart (\\r\\n is one CRLF)");
            Check(LineEndings.Detect("a\r\nb\r\n", LineEnding.Lf) == LineEnding.CrLf, "Detect CRLF");
            Check(LineEndings.Detect("a\nb\n", LineEnding.CrLf) == LineEnding.Lf, "Detect LF");
            Check(LineEndings.Detect("a\rb\r", LineEnding.CrLf) == LineEnding.Cr, "Detect CR");
            Check(LineEndings.Detect("a\nb\nc\r\n", LineEnding.CrLf) == LineEnding.Lf && LineEndings.Detect("a\r\nb\r\nc\n\r", LineEnding.Lf) == LineEnding.CrLf, "Mixed: the majority wins");
            Check(LineEndings.Detect("a\nb\r\n", LineEnding.Lf) == LineEnding.Lf && LineEndings.Detect("a\nb\r\n", LineEnding.CrLf) == LineEnding.CrLf && LineEndings.Detect("a\nb\r", LineEnding.CrLf) == LineEnding.Lf, "A tie prefers the default, else LF over CR");
            Check(LineEndings.Detect("one line", LineEnding.Lf) == LineEnding.Lf && LineEndings.Detect("", LineEnding.CrLf) == LineEnding.CrLf, "No line break: the default ending");
            Check(LineEndings.Count("a\nb\r\n").IsMixed && !LineEndings.Count("a\nb\n").IsMixed && !LineEndings.Count("x").IsMixed, "IsMixed only with two or more kinds");
            Check(LineEndings.Normalize("a\r\nb\nc\rd", LineEnding.Lf) == "a\nb\nc\nd" && LineEndings.Normalize("a\nb\r", LineEnding.CrLf) == "a\r\nb\r\n" && LineEndings.Normalize("a\r\n\nb", LineEnding.Cr) == "a\r\rb", "Normalize converts every break");
            Check(LineEndings.Os == (OperatingSystem.IsWindows() ? LineEnding.CrLf : LineEnding.Lf), "The OS line ending (CRLF on Windows)");

            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            var status = w.GetVisualDescendants().OfType<BottomStatusBar>().Single();
            TextBlock S(string name) => status.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == name);
            void Open(string path) => Wait((Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(v, [path])!);
            NoteDocument Active() => (NoteDocument)typeof(MainView).GetField("_active", Flags)!.GetValue(v)!;
            bool Save(NoteDocument d) => Result((Task<bool>)typeof(MainView).GetMethod("SaveAsync", Flags)!.Invoke(v, [d, false])!);
            bool SetEol(NoteDocument d, LineEnding e) => (bool)typeof(MainView).GetMethod("SetLineEnding", Flags)!.Invoke(v, [d, e, null])!;
            bool Normalize(NoteDocument d) => (bool)typeof(MainView).GetMethod("NormalizeLineEndings", Flags)!.Invoke(v, [d])!;
            NoteDocument New() => (NoteDocument)typeof(MainView).GetMethod("AddDocument", Flags)!.Invoke(v, [null, "", null, null])!;
            void Settle() { for (var i = 0; i < 3; i++) Pump(); }

            // --- Open, status bar, round-trip save per ending ---
            foreach (var (ending, eol) in new[] { (LineEnding.CrLf, "\r\n"), (LineEnding.Lf, "\n"), (LineEnding.Cr, "\r") })
            {
                var label = LineEndings.Label(ending);
                var text = $"# {label}{eol}line 2{eol}line 3{eol}";
                var path = F($"rt-{label}.md", text);
                Open(path);
                var doc = Active();
                Check(doc.LineEnding == ending && doc.Text == text && !doc.IsDirty, $"{label}: detected on open, the text is kept as is");
                Check(S("LineEndingLabel").Text == label && S("LineEndingLabel").IsVisible && !S("MixedLineEndingsLabel").IsVisible, $"{label}: the status bar shows '{S("LineEndingLabel").Text}', no mixed warning");
                Check(Save(doc) && File.ReadAllBytes(path).SequenceEqual(Encoding.UTF8.GetBytes(text)), $"{label}: an unchanged save writes identical bytes");
                doc.Editor.Text = text + "edited" + eol;
                Check(Save(doc) && File.ReadAllText(path) == text + "edited" + eol, $"{label}: an edited save keeps the ending");
                // Enter types the file's ending.
                var ed = doc.Editor; ed.Focus(); Settle();
                ed.CaretIndex = ed.Text!.Length; Settle();
                w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Settle();
                w.KeyTextInput("typed"); Settle();
                Check(doc.Text == text + "edited" + eol + eol + "typed", $"{label}: Enter inserts {label}");
                // Paste takes the file's ending, whatever the clipboard has.
                var clipboard = TopLevel.GetTopLevel(w)!.Clipboard!;
                Wait(clipboard.SetTextAsync("p1\r\np2\np3\rp4"));
                ed.CaretIndex = ed.Text!.Length; Settle();
                ed.Paste(); Settle(); Settle();
                Check(doc.Text.EndsWith("typed" + $"p1{eol}p2{eol}p3{eol}p4") && !doc.LineEndingCounts.IsMixed, $"{label}: pasted lines use {label}: {doc.Text.Replace("\r", "\\r").Replace("\n", "\\n")}");
                Check(Save(doc) && File.ReadAllText(path) == doc.Text && LineEndings.Count(File.ReadAllText(path)) is { IsMixed: false }, $"{label}: saving the typed and pasted lines keeps one ending");
            }
            ((MarkdownTextBox)Active().Editor).InsertText("x\ny");
            Check(Active().Text.EndsWith("x\ry"), "InsertText (the paste path) normalizes to the file's ending (CR file)");

            // --- Switching the ending converts the text and marks the file modified ---
            var lfPath = F("switch.md", "a\nb\nc");
            Open(lfPath);
            var sw = Active();
            sw.Editor.Focus(); Settle();
            sw.Editor.SelectionStart = 4; sw.Editor.SelectionEnd = 5; // "c"
            Check(!sw.IsDirty && SetEol(sw, LineEnding.CrLf) && sw.Text == "a\r\nb\r\nc" && sw.LineEnding == LineEnding.CrLf, "Switching to CRLF converts every line break");
            Check(sw.IsDirty && S("LineEndingLabel").Text == "CRLF" && sw.Title != IOPath.GetFileName(lfPath), "Switching marks the file modified and updates the status bar");
            Check(sw.Editor.SelectionStart == 6 && sw.Editor.SelectionEnd == 7, $"The selection follows its text ({sw.Editor.SelectionStart}-{sw.Editor.SelectionEnd})");
            Check(Save(sw) && File.ReadAllText(lfPath) == "a\r\nb\r\nc" && !sw.IsDirty, "Saving writes CRLF and clears the modified mark");
            Check(SetEol(sw, LineEnding.Lf) && sw.IsDirty && sw.Editor.CanUndo, "Switching back is undoable");
            sw.Editor.Undo(); Settle();
            Check(sw.Text == "a\r\nb\r\nc", "Undo restores the previous line breaks in one step");
            SetEol(sw, LineEnding.CrLf); Save(sw);
            var single = F("single.txt", "no break");
            Open(single);
            var one = Active();
            Check(SetEol(one, one.LineEnding == LineEnding.Lf ? LineEnding.Cr : LineEnding.Lf) && one.Text == "no break" && one.IsDirty, "A file without line breaks: switching still marks it modified");
            Check(Save(one) && !one.IsDirty, "and saving clears it");
            var menu = (MenuFlyout)typeof(MainView).GetMethod("BuildLineEndingMenu", Flags)!.Invoke(v, [one])!;
            var items = menu.Items.OfType<MenuItem>().ToList();
            Check(items.Select(i => i.Name).SequenceEqual(["LineEndingMenuHeader", "LineEndingCRLF", "LineEndingLF", "LineEndingCR"]) && items.Count(i => i.IsChecked) == 1 && (LineEnding)items.Single(i => i.IsChecked).Tag! == one.LineEnding, "The line ending menu lists CRLF, LF and CR with the current one checked");
            items.Single(i => i.Name == "LineEndingCRLF").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent)); Settle();
            Check(one.LineEnding == LineEnding.CrLf && one.IsDirty, "Clicking a menu item switches the ending");
            Check(ToolTip.GetTip(S("LineEndingLabel")) as string == Loc.T("LineEndingMenuTip") && S("LineEndingLabel").Cursor is not null, "The line ending label is clickable (hand cursor, tooltip)");

            // --- Mixed line endings: warning and normalization ---
            var mixedText = "a\r\nb\r\nc\nd\re\r\n";
            var mixedPath = F("mixed.md", mixedText);
            Open(mixedPath);
            var mixed = Active();
            Check(mixed.LineEnding == LineEnding.CrLf && mixed.Text == mixedText && !mixed.IsDirty, "A mixed file opens with the majority ending (CRLF), the text untouched");
            Check(S("MixedLineEndingsLabel").IsVisible && status.ShowsMixedLineEndings && S("MixedLineEndingsLabel").Text!.Contains(Loc.T("MixedLineEndings")), "The status bar warns about mixed line endings");
            var tip = ToolTip.GetTip(S("MixedLineEndingsLabel")) as string ?? "";
            Check(tip.Contains("CRLF 3") && tip.Contains("LF 1") && tip.Contains("CR 1") && tip.Contains("CRLF."), $"The warning tooltip counts each ending and offers CRLF: {tip}");
            Check(Save(mixed) && File.ReadAllText(mixedPath) == mixedText, "Saving a mixed file without normalizing keeps every line's own ending");
            Check(Normalize(mixed) && mixed.Text == "a\r\nb\r\nc\r\nd\r\ne\r\n" && mixed.IsDirty && !S("MixedLineEndingsLabel").IsVisible, "Normalize converts to the majority, marks modified and hides the warning");
            Check(!Normalize(mixed), "Normalizing a consistent file does nothing");
            Check(Save(mixed) && File.ReadAllText(mixedPath) == "a\r\nb\r\nc\r\nd\r\ne\r\n", "The normalized file saves with CRLF");
            var lfMajority = F("mixed-lf.txt", "1\n2\n3\r\n");
            Open(lfMajority);
            Check(Active().LineEnding == LineEnding.Lf && Normalize(Active()) && Active().Text == "1\n2\n3\n", "An LF-majority file normalizes to LF");
            // Item 72's line moves keep each line's own terminator in a mixed file.
            var moved = LineEdits.Move("a\r\nb\nc", 0, 0, 1)!.Value;
            Check(string.Concat("a\r\nb\nc".AsSpan(0, moved.Start), moved.Text, "a\r\nb\nc".AsSpan(moved.Start + moved.Length)) == "b\r\na\nc", "Item 72's Move keeps the per-slot endings");
            Check(LineEdits.Duplicate("a", 0, 0, "\r").Text == "\ra" && LineEdits.Duplicate("a\nb", 3, 3, "\r\n").Text == "\r\nb", "Ctrl+D on a last line without a newline uses the document's ending");

            // --- New files: the OS ending by default, then the setting ---
            Check(LineEndingSettings.DefaultLineEndingId == "os" && New().LineEnding == LineEndings.Os && S("LineEndingLabel").Text == LineEndings.Label(LineEndings.Os), "New files use the OS line ending by default");
            SessionManager.Enabled = true;
            var panel = EditorFontsFlyout.BuildEncoding();
            var box = panel.Children.OfType<ComboBox>().Single(c => c.Name == "DefaultLineEndingBox");
            var encodingBox = panel.Children.OfType<ComboBox>().Single(c => c.Name == "DefaultEncodingBox");
            Check(panel.Children.IndexOf(box) > panel.Children.IndexOf(encodingBox) && panel.Children.OfType<TextBlock>().Any(t => t.Text == Loc.T("DefaultLineEnding")), "The Default line ending setting sits next to Default encoding in the editor gear");
            Check(box.ItemsSource!.Cast<string>().SequenceEqual([Loc.F("LineEndingOs", LineEndings.Label(LineEndings.Os)), "CRLF", "LF"]) && box.SelectedIndex == 0, "Options: OS (with its ending), CRLF, LF; OS selected");
            box.SelectedIndex = 2;
            Check(LineEndingSettings.DefaultLineEndingId == "lf" && SessionStore.Load(sessionPath)?.DefaultLineEnding == "lf", "Picking LF saves it in the session");
            var lfNew = New();
            Check(lfNew.LineEnding == LineEnding.Lf && lfNew.Editor.NewLine == "\n" && !lfNew.IsDirty, "New files then use LF");
            box.SelectedIndex = 1;
            Check(New().LineEnding == LineEnding.CrLf && SessionStore.Load(sessionPath)?.DefaultLineEnding == "crlf", "and CRLF");
            var noBreak = F("nobreak2.txt", "plain");
            Open(noBreak);
            Check(Active().LineEnding == LineEnding.CrLf, "A file without line breaks uses the setting too");
            Open(lfPath.Replace("switch.md", "rt-LF.md"));
            Check(Active().LineEnding == LineEnding.Lf, "Files with line breaks keep their detected ending");
            LineEndingSettings.ImportDefault(null);
            Check(LineEndingSettings.DefaultLineEndingId == "os", "Missing in an old session: OS");
            LineEndingSettings.ImportDefault("LF");
            Check(LineEndingSettings.DefaultLineEndingId == "lf", "The saved setting is restored");
            LineEndingSettings.ImportDefault("bogus");
            Check(LineEndingSettings.DefaultLineEndingId == "os", "An unknown value falls back to OS");
            LineEndingSettings.SetDefault("lf"); LineEndingSettings.SetDefault(LineEndingSettings.DefaultId);
            Check(SessionStore.Load(sessionPath)?.DefaultLineEnding is null, "OS is not written (null in the session)");

            // --- Localization and theme ---
            string[] keys = ["LineEndingMenuTip", "MixedLineEndings", "MixedLineEndingsTip", "MixedLineEndingsReadOnlyTip", "LineEndingChanged", "LineEndingsNormalized", "DefaultLineEnding", "DefaultLineEndingTip", "LineEndingOs"];
            foreach (var code in Loc.Languages.Select(l => l.Code))
            {
                var table = Loc.Table(code);
                Check(keys.All(k => table.TryGetValue(k, out var s) && !string.IsNullOrWhiteSpace(s) && (code == Loc.English || s != Loc.Table(Loc.English)[k])), $"{code}: all item 68 strings are translated");
                var formatted = string.Format(table["MixedLineEndingsTip"], 7, 8, 9, "CRLF");
                Check(formatted.Contains('7') && formatted.Contains('8') && formatted.Contains('9') && formatted.Contains("CRLF") && string.Format(table["LineEndingOs"], "LF").Contains("LF") && string.Format(table["LineEndingsNormalized"], "LF").Contains("LF"), $"{code}: placeholders are kept");
            }
            Loc.Set("ja"); Settle();
            Open(mixedPath.Replace("mixed.md", "mixed-lf.txt"));
            File.WriteAllText(IOPath.Combine(root, "mixed-ja.txt"), "1\n2\r\n3\n");
            Open(IOPath.Combine(root, "mixed-ja.txt"));
            Check(S("MixedLineEndingsLabel").Text!.Contains(Loc.Table("ja")["MixedLineEndings"]), "The warning follows the UI language");
            Loc.Set(Loc.English);
            var xaml = File.ReadAllText(IOPath.Combine(Repo(), "Components", "BottomStatusBar.axaml"));
            Check(!System.Text.RegularExpressions.Regex.IsMatch(xaml, "#[0-9A-Fa-f]{6}") && xaml.Contains("x:Name=\"LineEndingLabel\"") && xaml.Contains("{DynamicResource ErrorForeground}"), "The line ending labels use theme resources only");
            foreach (var d in w.GetVisualDescendants().OfType<FileTabs>().Single().FindControl<ListBox>("Tabs")!.Items.OfType<NoteDocument>()) { d.SavedText = d.Text; d.SavedLineEnding = d.LineEnding; }
            Console.WriteLine($"Line ending tests: {passed} passed");
        }
        finally
        {
            LineEndingSettings.ImportDefault(null);
            SessionManager.Enabled = false; SessionManager.PathOverride = null;
            Loc.Set(startLanguage);
            w.Close();
            try { Directory.Delete(root, true); } catch { }
            try { File.Delete(sessionPath); } catch { }
        }
    }

    static string Repo([System.Runtime.CompilerServices.CallerFilePath] string path = "") => IOPath.GetFullPath(IOPath.Combine(IOPath.GetDirectoryName(path)!, "..", ".."));
}