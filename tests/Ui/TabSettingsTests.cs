using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Text;
using Mdhub.Views;

// Item 70: editor gear 'Insert spaces' (default on) and 'Tab size' (1-8, default 4) drive Tab, Shift+Tab, block
// indent/outdent and the width of tab characters; saved in the session.
internal static class TabSettingsTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Settle() { for (var i = 0; i < 8; i++) Pump(); }
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var sessionPath = Path.Combine(Path.GetTempPath(), $"mdhub-tabs-{Guid.NewGuid():N}.json");
        SessionManager.PathOverride = sessionPath; SessionManager.Enabled = true;
        EditorBehavior.Import(null);

        // ---- Pure rules ----
        CaretEdit? A(string t, int s, int e, bool o, bool sp = true, int n = 4) => TabEditing.Apply(t, s, e, o, sp, n);
        string Do(string t, int s, int e, bool o, bool sp = true, int n = 4) => A(t, s, e, o, sp, n) is { } x ? t[..x.Start] + x.Replacement + t[x.End..] : t;
        Check(EditorBehavior.Current is { InsertSpaces: true, TabSize: 4 } && EditorBehavior.IsDefault, "Defaults: insert spaces on, tab size 4");
        Check(Do("ab", 0, 0, false) == "    ab" && Do("ab", 2, 2, false) == "ab  " && Do("ab", 1, 1, false, n: 3) == "a  b", "Spaces fill to the next tab stop (size 4 and 3)");
        Check(Do("ab", 1, 1, false, sp: false) == "a\tb", "Insert spaces off: Tab inserts a tab character");
        Check(Do("\tx", 1, 1, false, n: 4) == "\t    x" && TabEditing.Column("\tab", 2, 4) == 5 && TabEditing.Column("a\tb", 2, 8) == 8, "Columns count existing tabs at the tab size");
        Check(Do("a1\nb2\nc3", 0, 7, false) == "    a1\n    b2\n    c3" && Do("a\nb", 0, 3, false, sp: false) == "\ta\n\tb", "Multi-line selection indents every line (spaces or tab)");
        Check(Do("a\n\nb", 0, 4, false) == "    a\n\n    b", "Block indent leaves blank lines empty");
        Check(Do("a\nb\nc", 0, 4, false) == "    a\n    b\nc", "A selection ending at a line start leaves that line alone");
        Check(Do("a\r\nb\r\nc", 0, 6, false) == "    a\r\n    b\r\nc", "CRLF text indents the same lines");
        Check(Do("      x", 7, 7, true) == "  x" && Do("  x", 3, 3, true) == "x" && Do("\t\tx", 2, 2, true) == "\tx" && Do("  \tx", 4, 4, true) == "x", "Shift+Tab removes one level (tab size spaces, a tab, or spaces then a tab)");
        Check(Do("        x", 8, 8, true, n: 2) == "      x", "Outdent follows the tab size");
        Check(A("x", 1, 1, true) is null, "Shift+Tab on an unindented line does nothing");
        Check(Do("    a\n  b\nc", 0, 10, true) == "a\nb\nc", "Multi-line outdent");
        Check(A("    ab", 6, 6, true) is { SelectionStart: 2, SelectionEnd: 2 } && A("ab", 1, 1, false) is { SelectionStart: 4 }, "The caret moves with the text");
        Check(A("a\nb", 0, 3, false) is { SelectionEnd: 11 } r && r.SelectionStart <= 4, "A block selection keeps covering the indented lines");
        Check(TabEditing.Clamp(0) == 1 && TabEditing.Clamp(9) == 8 && TabEditing.Clamp(-5) == 1 && TabEditing.Clamp(3) == 3, "Tab size clamps to 1-8");
        EditorBehavior.Import(new EditorBehaviorData { TabSize = 42 }); Check(EditorBehavior.Current.TabSize == 8, "A saved size over 8 loads as 8");
        EditorBehavior.Import(new EditorBehaviorData { TabSize = 0 }); Check(EditorBehavior.Current.TabSize == 1, "A saved size under 1 loads as 1");
        EditorBehavior.Import(null);
        var sc = Shortcuts.All.Single(s => s.Id == "editor.outdent");
        Check(sc.HandledByTextBox && sc.Defaults[0].Key == "Tab" && sc.Defaults[0].Mods == ShortcutMods.Shift && Loc.Languages.All(l => Loc.Table(l.Code).ContainsKey("Sc.editor.outdent")), "Shift+Tab 'Outdent' is in the shortcut registry, localized");
        Check(Loc.Languages.Where(l => l.Code != Loc.English).All(l => new[] { "EditorTabSettings", "EditorInsertSpaces", "EditorTabSize" }.All(k => Loc.Table(l.Code).TryGetValue(k, out var v) && v != Loc.En[k])),
            $"The tab settings are translated in all {Loc.Languages.Count} languages");

        var w = new MainWindow { Width = 1100, Height = 760 };
        try
        {
            w.Show(); Settle();
            var view = w.GetVisualDescendants().OfType<MainView>().Single();
            var doc = (NoteDocument)typeof(MainView).GetMethod("AddDocument", flags)!.Invoke(view, [null, "line one\nline two", "tabs.md", null])!;
            Settle();
            var ed = doc.Editor; ed.Focus(); Settle();
            void Key(bool shift) { w.KeyPress(Avalonia.Input.Key.Tab, shift ? RawInputModifiers.Shift : RawInputModifiers.None, PhysicalKey.Tab, null); Settle(); }

            // ---- Typing ----
            ed.CaretIndex = 0; Key(false);
            Check(ed.Text == "    line one\nline two" && ed.CaretIndex == 4 && ed.IsFocused, "Tab inserts 4 spaces by default and keeps focus");
            Key(true);
            Check(ed.Text == "line one\nline two" && ed.CaretIndex == 0 && ed.IsFocused, "Shift+Tab outdents and keeps focus (no focus move)");
            ed.SelectionStart = 0; ed.SelectionEnd = ed.Text!.Length; Key(false);
            Check(ed.Text == "    line one\n    line two", "Tab with a multi-line selection indents both lines");
            ed.Undo(); Settle();
            Check(ed.Text == "line one\nline two", "Block indent is one undo step");
            ed.SelectionStart = 0; ed.SelectionEnd = ed.Text!.Length; Key(false); ed.SelectionStart = 0; ed.SelectionEnd = ed.Text!.Length; Key(true);
            Check(ed.Text == "line one\nline two", "Shift+Tab with a multi-line selection outdents both lines");

            // ---- Gear ----
            var gear = w.GetVisualDescendants().OfType<Button>().First(b => b.Name == "EditorSettingsButton");
            var flyout = (Flyout)gear.Flyout!;
            flyout.ShowAt(gear); Settle();
            var content = (Control)flyout.Content!;
            var spaces = content.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "EditorInsertSpacesBox");
            var size = content.GetVisualDescendants().OfType<NumericUpDown>().Single(c => c.Name == "EditorTabSizeBox");
            Check(spaces.IsChecked == true && spaces.Content as string == Loc.T("EditorInsertSpaces") && size.Value == 4 && size.Minimum == 1 && size.Maximum == 8,
                "The editor gear has 'Insert spaces' (on) and 'Tab size' 4 (1-8)");
            size.Value = 2; Settle();
            Check(EditorBehavior.Current.TabSize == 2, "Changing the tab size applies live");
            ed.Focus(); ed.CaretIndex = 0; Key(false);
            Check(ed.Text == "  line one\nline two", "Tab now inserts 2 spaces");
            Key(true); EditorBehavior.Update(d => d with { TabSize = 12 }); Settle();
            Check(EditorBehavior.Current.TabSize == 8, "A tab size over 8 clamps to 8");
            size.Value = 4; spaces.IsChecked = false; Settle();
            Check(!EditorBehavior.Current.InsertSpaces, "Unticking 'Insert spaces' applies live");
            ed.Focus(); ed.CaretIndex = 0; Key(false);
            Check(ed.Text == "\tline one\nline two", "Tab inserts a tab character with spaces off");
            Key(true);
            Check(ed.Text == "line one\nline two", "Shift+Tab removes a leading tab");

            // ---- Display width of tabs ----
            ed.Text = "\tx"; Settle();
            var presenter = ed.GetVisualDescendants().OfType<MarkdownTextPresenter>().Single();
            double X() { presenter.InvalidateMeasure(); Settle(); return presenter.TextLayout.HitTestTextPosition(1).X; }
            size.Value = 2; Settle(); var x2 = X();
            size.Value = 8; Settle(); var x8 = X();
            using var spaceLayout = new Avalonia.Media.TextFormatting.TextLayout(" ", new Avalonia.Media.Typeface(ed.FontFamily), ed.FontSize, null);
            var sw = spaceLayout.WidthIncludingTrailingWhitespace;
            Check(Math.Abs(x2 - 2 * sw) < 1.5 && Math.Abs(x8 - 8 * sw) < 1.5, $"A tab character is tab-size spaces wide ({x2:0.#} / {x8:0.#}, space {sw:0.#})");
            var plain = (NoteDocument)typeof(MainView).GetMethod("AddDocument", flags)!.Invoke(view, [null, "\ty", "plain.txt", null])!;
            Settle();
            var pp = plain.Editor.GetVisualDescendants().OfType<MarkdownTextPresenter>().FirstOrDefault();
            Check(pp is null || Math.Abs(pp.TextLayout.HitTestTextPosition(1).X - 8 * sw) < 1.5, "Plain-text files use the same tab width");
            flyout.Hide(); Settle();

            // ---- Persistence round-trip ----
            var saved = SessionStore.Load(sessionPath)?.EditorBehavior;
            Check(saved is { InsertSpaces: false, TabSize: 8 }, "Both settings are saved in the session");
            EditorBehavior.Import(null);
            EditorBehavior.Import(saved);
            Check(EditorBehavior.Current is { InsertSpaces: false, TabSize: 8 }, "Loading the session restores them");
            EditorBehavior.Update(_ => new EditorBehaviorData()); Settle();
            Check(SessionStore.Load(sessionPath)?.EditorBehavior is null, "All-default settings are not written");
        }
        finally
        {
            EditorBehavior.Import(null);
            foreach (var d in w.GetVisualDescendants().OfType<FileTabs>().Single().FindControl<ListBox>("Tabs")!.Items.OfType<NoteDocument>()) d.SavedText = d.Text;
            w.Close();
            try { File.Delete(sessionPath); } catch { }
        }
        Console.WriteLine($"Tab settings tests: {passed} passed");
    }
}
