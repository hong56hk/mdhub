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
using Mdhub.Text;
using Mdhub.Views;

// Item 71: Markdown list and table auto-complete (Services/MarkdownAutoComplete) through the editor's thin key hooks.
internal static class AutoCompleteTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Settle() { for (var i = 0; i < 8; i++) Pump(); }
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var sessionPath = Path.Combine(Path.GetTempPath(), $"mdhub-autocomplete-{Guid.NewGuid():N}.json");
        SessionManager.PathOverride = sessionPath; SessionManager.Enabled = true;
        EditorBehavior.Import(null);
        var o = MarkdownTextBox.AutoOptions;

        // ---- Pure rules (caret marked with ‸) ----
        string Show(string t, CaretEdit? e) { if (e is not { } x) return "<none>"; var r = t[..x.Start] + x.Replacement + t[x.End..]; return r.Insert(Math.Clamp(x.SelectionStart, 0, r.Length), "‸"); }
        void Rule(string input, string expect, string name, Func<string, int, CaretEdit?> f)
        {
            var c = input.IndexOf('‸'); var t = input.Remove(c, 1);
            var got = Show(t, f(t, c));
            Check(got == expect, $"{name} ({got.Replace("\n", "\\n")})");
        }
        CaretEdit? En(string t, int c) => MarkdownAutoComplete.Enter(t, c, o, "\n");
        CaretEdit? Tb(string t, int c) => MarkdownAutoComplete.Tab(t, c, c, false, o, "\n");
        CaretEdit? STb(string t, int c) => MarkdownAutoComplete.Tab(t, c, c, true, o, "\n");
        CaretEdit? Bs(string t, int c) => MarkdownAutoComplete.Backspace(t, c, o);
        Check(EditorBehavior.Current is { AutoLists: true, AutoTables: true, AlignTables: true }, "Defaults: lists, tables and alignment on");
        Rule("- a‸", "- a\n- ‸", "Enter continues '- '", En);
        Rule("* a‸", "* a\n* ‸", "Enter continues '* '", En);
        Rule("  + a‸", "  + a\n  + ‸", "Enter continues '+ ' at the same indent", En);
        Rule("- [x] done‸", "- [x] done\n- [ ] ‸", "Task items continue as '- [ ] '", En);
        Rule("- ab‸cd", "- ab\n- ‸cd", "Enter mid-item carries the rest to the new item", En);
        Rule("- a\n- ‸", "- a\n‸", "Enter on an empty item ends the list", En);
        Rule("- a\n    - ‸", "- a\n- ‸", "Enter on an empty indented item outdents one level", En);
        Rule("1. a‸", "1. a\n2. ‸", "Numbered: next number with '.'", En);
        Rule("3) a‸", "3) a\n4) ‸", "Numbered: keeps the ')' style", En);
        Rule("1. a‸\n2. b\n3. c", "1. a\n2. ‸\n3. b\n4. c", "Inserting mid-list renumbers the following items", En);
        Rule("1. a\n    1. ‸", "1. a\n2. ‸", "An empty sub-item outdents and takes the parent's next number", En);
        Rule("- a‸", "    - a‸", "Tab indents a list line", Tb);
        Rule("    - a‸", "- a‸", "Shift+Tab outdents a list line", STb);
        Rule("1. a\n2. b‸\n3. c", "1. a\n    1. b‸\n2. c", "An indented item starts a sub-list at 1 (parent renumbered)", Tb);
        Rule("1. a\n    1. b‸\n2. c", "1. a\n2. b‸\n3. c", "Outdenting rejoins the parent list", STb);
        Rule("- ‸", "‸", "Backspace after an empty marker removes just the marker", Bs);
        Rule("  - [ ] ‸", "  ‸", "Backspace removes an empty task marker, keeping the indent", Bs);
        Rule("- a‸", "<none>", "Backspace elsewhere is a normal backspace", Bs);
        Check(MarkdownAutoComplete.RenumberAt("1. a\n3. c\n4. d", 5, o) is { } re && Show("1. a\n3. c\n4. d", re).Replace("‸", "") == "1. a\n2. c\n3. d", "Deleting an item mid-list renumbers the rest");
        Rule("---‸", "<none>", "A thematic break is not a list", En);
        Rule("```\n- a‸", "<none>", "Off inside fenced code", En);
        Rule("~~~\n| a |‸", "<none>", "Tables off inside ~~~ fences", En);
        Rule("```\n- a\n```\n- b‸", "```\n- a\n```\n- b\n- ‸", "On again after the fence closes", En);
        Rule("| a | b |‸", "| a | b |\n| --- | --- |\n| ‸ |  |", "Enter after a header row inserts the delimiter and an empty row", En);
        Rule("| a | b |\n| --- | --- |\n| 1 | 2 |‸", "| a | b |\n| --- | --- |\n| 1 | 2 |\n| ‸ |  |", "Enter on a row adds an empty row", En);
        Rule("| a | b |\n| --- | --- |\n| 1‸ | 2 |", "| a | b |\n| --- | --- |\n| 1 | 2 |\n| ‸ |  |", "Enter inside a row adds an empty row below", En);
        Rule("| a‸ | b |", "| a | ‸b |", "Tab moves to the next cell", Tb);
        Rule("| a | b‸ |\n| --- | --- |\n| 1 | 2 |", "| a | b |\n| --- | --- |\n| ‸1 | 2 |", "Tab in a row's last cell moves to the next row (skipping the delimiter)", Tb);
        Rule("| a | b |\n| --- | --- |\n| 1 | 2‸ |", "| a | b |\n| --- | --- |\n| 1 | 2 |\n| ‸ |  |", "Tab in the table's last cell adds a row", Tb);
        Rule("| a | b‸ |", "| ‸a | b |", "Shift+Tab moves to the previous cell", STb);
        Rule("| a | b |\n| --- | --- |\n| ‸1 | 2 |", "| a | ‸b |\n| --- | --- |\n| 1 | 2 |", "Shift+Tab from a first cell goes to the previous row's last cell", STb);
        Check(MarkdownAutoComplete.DisplayWidth("\u4e2da\ud55c") == 5 && MarkdownAutoComplete.DisplayWidth("\uff21") == 2 && MarkdownAutoComplete.DisplayWidth("e\u0301") == 1, "Display width: CJK and fullwidth count 2, combining marks 0");
        var aligned = MarkdownAutoComplete.AlignRows(["| a | bb |", "|-|:-:|", "| \u4e2d\u6587 | x |"]);
        Check(aligned == "| a    | bb  |\n| ---- | :-: |\n| \u4e2d\u6587 | x   |", "Alignment pads by display width and keeps alignment colons");
        var off = o with { Lists = false, Tables = false };
        Check(MarkdownAutoComplete.Enter("- a", 3, off) is null && MarkdownAutoComplete.Enter("| a |", 5, off) is null && MarkdownAutoComplete.Tab("| a |", 2, 2, false, off) is null && MarkdownAutoComplete.Backspace("- ", 2, off) is null, "Switched off: no rule applies");
        Check(MarkdownAutoComplete.AlignTable("| a |\n|---|\n| b |", 0, 20, o with { Align = false }) is null, "Alignment off: tables stay as typed");
        Check(Loc.Languages.Where(l => l.Code != Loc.English).All(l => new[] { "EditorAutoComplete", "EditorAutoLists", "EditorAutoTables", "EditorAlignTables" }.All(k => Loc.Table(l.Code).TryGetValue(k, out var v) && v != Loc.En[k])),
            $"The auto-complete settings are translated in all {Loc.Languages.Count} languages");

        var w = new MainWindow { Width = 1100, Height = 760 };
        try
        {
            w.Show(); Settle();
            var view = w.GetVisualDescendants().OfType<MainView>().Single();
            NoteDocument Open(string text, string name) { var d = (NoteDocument)typeof(MainView).GetMethod("AddDocument", flags)!.Invoke(view, [null, text, name, null])!; Settle(); d.Editor.Focus(); Settle(); return d; }
            var doc = Open("- a", "ac.md");
            var ed = doc.Editor;
            string T() => (ed.Text ?? "").Replace("\r\n", "\n");
            void Key(Key k, RawInputModifiers m = RawInputModifiers.None) { w.KeyPress(k, m, k == Avalonia.Input.Key.Tab ? PhysicalKey.Tab : k == Avalonia.Input.Key.Back ? PhysicalKey.Backspace : PhysicalKey.Enter, null); Settle(); }
            void Type(string s) { ed.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = s }); Settle(); }
            void Set(string text, int caret) { ed.Text = text; Settle(); ed.CaretIndex = caret; Settle(); }

            ed.CaretIndex = 3; Key(Avalonia.Input.Key.Enter);
            Check(T() == "- a\n- " && ed.CaretIndex == ed.Text!.Length, "Editor: Enter continues the bullet");
            ed.Undo(); Settle();
            Check(T() == "- a\n", "Ctrl+Z after a completion leaves a plain newline");
            ed.Undo(); Settle();
            Check(T() == "- a", "A second Ctrl+Z removes the newline");
            ed.CaretIndex = 3; Key(Avalonia.Input.Key.Enter, RawInputModifiers.Shift);
            Check(T() == "- a\n", "Shift+Enter is a plain newline");
            Set("- a\n- ", 6); Key(Avalonia.Input.Key.Enter);
            Check(T() == "- a\n", "Editor: Enter on an empty item ends the list");
            Set("- a\n- ", 6); Key(Avalonia.Input.Key.Back);
            Check(T() == "- a\n" && ed.CaretIndex == 4, "Editor: Backspace removes just the marker");
            Set("- a", 3); Key(Avalonia.Input.Key.Tab);
            Check(T() == "    - a" && ed.CaretIndex == 7, "Editor: Tab indents the list line");
            Key(Avalonia.Input.Key.Tab, RawInputModifiers.Shift);
            Check(T() == "- a" && ed.CaretIndex == 3, "Editor: Shift+Tab outdents it");
            Set("1. a\n2. b\n3. c", 4); Key(Avalonia.Input.Key.Enter);
            Check(T() == "1. a\n2. \n3. b\n4. c", "Editor: Enter mid-list renumbers");
            ed.Undo(); Settle();
            Check(T() == "1. a\n\n2. b\n3. c", "One undo leaves the plain newline (completion and renumbering are one step)");
            Set("1. a\n2. b\n3. c\n4. d", 10); // caret at the start of '3. c' line
            ed.SelectionStart = 10; ed.SelectionEnd = 15; Key(Avalonia.Input.Key.Back);
            Check(T() == "1. a\n2. b\n3. d", "Editor: deleting an item renumbers the rest");

            // Tables.
            Set("| a | b |", 9); Key(Avalonia.Input.Key.Enter);
            Check(T() == "| a | b |\n| --- | --- |\n|  |  |" && ed.Text![..ed.CaretIndex].Replace("\r\n", "\n").EndsWith("\n| "), $"Editor: Enter after a header adds the delimiter and an empty row ({T().Replace("\n","\\n")} @{ed.CaretIndex})");
            Type("x"); Key(Avalonia.Input.Key.Tab); Type("\u4e2d\u6587");
            Check(T() == "| a | b |\n| --- | --- |\n| x | \u4e2d\u6587 |", "Editor: Tab moves to the next cell");
            Key(Avalonia.Input.Key.Tab);
            Check(T().EndsWith("\n|  |  |"), "Editor: Tab in the last cell adds a row");
            Key(Avalonia.Input.Key.Tab, RawInputModifiers.Shift);
            var beforeLeave = T();
            ed.Text = ed.Text + "\n\nafter"; Settle();
            ed.CaretIndex = ed.Text!.Length; Settle(); Settle();
            Check(T().StartsWith("| a   | b    |\n| --- | ---- |\n| x   | \u4e2d\u6587 |\n|     |      |"), $"Leaving the table aligns pipes and widths, CJK counted as 2 ({T().Replace("\n", "\\n")})");
            Check(ed.CaretIndex == ed.Text!.Length, "The caret stays where it went");
            ed.Undo(); Settle();
            Check(!T().StartsWith("| a   |"), "Alignment is one undo step");

            // Edge cases.
            Set("```\n- a", 7); Key(Avalonia.Input.Key.Enter);
            Check(T() == "```\n- a\n", "Editor: inside fenced code Enter is plain");
            var txt = Open("- a", "plain.txt"); var te = txt.Editor;
            te.CaretIndex = 3; w.KeyPress(Avalonia.Input.Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Settle();
            Check((te.Text ?? "").Replace("\r\n", "\n") == "- a\n", "Non-Markdown files: no auto-complete");
            doc.Editor.Focus(); Settle();

            // ---- Gear ----
            var gear = w.GetVisualDescendants().OfType<Button>().First(b => b.Name == "EditorSettingsButton");
            var flyout = (Flyout)gear.Flyout!;
            flyout.ShowAt(gear); Settle();
            var content = (Control)flyout.Content!;
            CheckBox Box(string n) => content.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == n);
            Check(Box("EditorAutoListsBox").IsChecked == true && Box("EditorAutoTablesBox").IsChecked == true && Box("EditorAlignTablesBox").IsChecked == true,
                "The editor gear has the three auto-complete checkboxes, on");
            Box("EditorAutoListsBox").IsChecked = false; Settle();
            flyout.Hide(); Settle(); doc.Editor.Focus(); ed.Focus(); Settle();
            Set("- a", 3); ed.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter }); Settle();
            Check(T() == "- a\n", $"Unticking 'Auto-complete lists' turns list completion off ({T().Replace("\n","\\n")} lists={EditorBehavior.Current.AutoLists} focus={ed.IsFocused})");
            flyout.ShowAt(gear); Settle(); content = (Control)flyout.Content!;
            Box("EditorAutoTablesBox").IsChecked = false; Box("EditorAlignTablesBox").IsChecked = false; Settle(); flyout.Hide(); Settle();
            var saved = SessionStore.Load(sessionPath)?.EditorBehavior;
            Check(saved is { AutoLists: false, AutoTables: false, AlignTables: false }, "The switches are saved in the session");
            EditorBehavior.Import(null); EditorBehavior.Import(saved);
            Check(EditorBehavior.Current is { AutoLists: false, AutoTables: false, AlignTables: false }, "Loading the session restores them");
            EditorBehavior.Update(_ => new EditorBehaviorData()); Settle();
            Check(SessionStore.Load(sessionPath)?.EditorBehavior is null, "All defaults are not written");
            foreach (var d in w.GetVisualDescendants().OfType<FileTabs>().Single().FindControl<ListBox>("Tabs")!.Items.OfType<NoteDocument>()) d.SavedText = d.Text;
        }
        finally
        {
            EditorBehavior.Import(null);
            w.Close();
            try { File.Delete(sessionPath); } catch { }
        }
        Console.WriteLine($"Auto-complete tests: {passed} passed");
    }
}
