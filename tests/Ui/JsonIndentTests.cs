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

// Item 113: VS Code-like JSON auto-indent (Text/JsonAutoIndent) through the editor's Enter / closer hooks.
internal static class JsonIndentTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS: " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Settle() { for (var i = 0; i < 8; i++) Pump(); }

        EditorBehavior.Import(null);

        Check(JsonAutoIndent.AppliesTo("data.json") && JsonAutoIndent.AppliesTo(@"C:\a\B.JSON")
              && !JsonAutoIndent.AppliesTo("data.jsonc") && !JsonAutoIndent.AppliesTo("data.json5")
              && !JsonAutoIndent.AppliesTo("note.md") && !JsonAutoIndent.AppliesTo("plain.txt")
              && !JsonAutoIndent.AppliesTo(null) && !JsonAutoIndent.AppliesTo(""),
            "Applies only to .json files, not .jsonc/.json5 or other text");

        Check(EditorBehavior.Current.AutoIndentJson
              && System.Text.Json.JsonSerializer.Deserialize<EditorBehaviorData>("{}")!.AutoIndentJson,
            "Auto indent JSON defaults on, including older sessions");

        string Show(string t, CaretEdit e, string nl = "\n")
        {
            var r = t[..e.Start] + e.Replacement + t[e.End..];
            return r.Insert(Math.Clamp(e.SelectionStart, 0, r.Length), "?").Replace(nl, nl == "\r\n" ? "\r\n" : "\n");
        }
        CaretEdit En(string t, int c, bool spaces, int size, string nl = "\n") => JsonAutoIndent.Enter(t, c, spaces, size, nl);
        void Rule(string input, string expect, string name, bool spaces = true, int size = 4, string nl = "\n")
        {
            var c = input.IndexOf('?');
            var t = input.Remove(c, 1);
            var got = Show(t, En(t, c, spaces, size, nl), nl);
            Check(got == expect, name + (got == expect ? "" : $" (got {got.Replace("\r", "\\r").Replace("\n", "\\n")})"));
        }

        // Rule 1: Enter after { or [ indents one level.
        Rule("{?", "{\n    ?", "Enter after { indents one level (4 spaces)");
        Rule("[?", "[\n    ?", "Enter after [ indents one level (4 spaces)");
        Rule("    \"a\": {?", "    \"a\": {\n        ?", "Enter after { keeps the line indent and adds one level");
        Rule("  {?", "  {\n    ?", "Enter after { with 2-space indent adds one 2-space level", spaces: true, size: 2);
        Rule("\t{?", "\t{\n\t\t?", "Enter after { with tabs adds one tab", spaces: false, size: 4);
        Rule("{\r\n    \"a\": [?", "{\r\n    \"a\": [\r\n        ?", "Enter after [ preserves CRLF", spaces: true, size: 4, nl: "\r\n");

        // Rule 2: Enter between {} or [] opens a middle line and aligns the closer with the opener.
        Rule("{?}", "{\n    ?\n}", "Enter between {} puts the caret on an indented middle line");
        Rule("[?]", "[\n    ?\n]", "Enter between [] puts the caret on an indented middle line");
        Rule("    {?}", "    {\n        ?\n    }", "Enter between {} aligns the closer with the opener's indent");
        Rule("    {  ?  }", "    {  \n        ?\n    }", "Enter between {} allows whitespace around the caret");
        Rule("  [?]", "  [\n    ?\n  ]", "Enter between [] with 2-space indent", spaces: true, size: 2);
        Rule("\t{?}", "\t{\n\t\t?\n\t}", "Enter between {} with tabs", spaces: false, size: 4);
        Rule("{?}\r\n", "{\r\n    ?\r\n}\r\n", "Enter between {} preserves CRLF", spaces: true, size: 4, nl: "\r\n");

        // Rule 4: otherwise Enter keeps the indentation of the line above (the current line).
        Rule("    \"a\": 1?", "    \"a\": 1\n    ?", "Enter after a value keeps the current indent");
        Rule("?", "\n?", "Enter at the start of an empty document inserts a newline");
        Rule("    { \"a\": 1?}", "    { \"a\": 1\n    ?}", "Enter before a closer on a non-empty line keeps the current indent");
        Rule("\"hello{?\"", "\"hello{\n?\"", "Enter after a brace inside a string does not add a level");
        Rule("    \"a\": \"x?y\"", "    \"a\": \"x\n    ?y\"", "Enter inside a string keeps the current indent");

        // Rule 3: typing } or ] on a whitespace-only line outdents to the opener.
        void CloseRule(string input, char closer, string expect, string name)
        {
            var c = input.IndexOf('?');
            var t = input.Remove(c, 1);
            var e = JsonAutoIndent.Close(t, c, closer);
            var got = e is { } x ? Show(t, x) : "<none>";
            Check(got == expect, name + (got == expect ? "" : $" (got {got.Replace("\n", "\\n")})"));
        }
        CloseRule("{\n    ?", '}', "{\n}?", "} on a whitespace line aligns with the opening {");
        CloseRule("{\n        ?", '}', "{\n}?", "} outdents more than one level to match the opener");
        CloseRule("    {\n        ?", '}', "    {\n    }?", "} on an indented object matches the opener's indent");
        CloseRule("[\n    ?", ']', "[\n]?", "] on a whitespace line aligns with the opening [");
        CloseRule("{\n    [ \n        ?", ']', "{\n    [ \n    ]?", "] matches its [ inside an object");
        CloseRule("{\n    ?", ']', "<none>", "] does not close a { (no matching opener)");
        CloseRule("{\n    \"a\": 1?", '}', "<none>", "} on a non-blank line is not auto-outdented");
        CloseRule("\"{ \n    ?", '}', "<none>", "} does not match a brace that is inside a string");

        // ---- Editor ----
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var sessionPath = Path.Combine(Path.GetTempPath(), $"mdhub-jsonindent-{Guid.NewGuid():N}.json");
        SessionManager.PathOverride = sessionPath; SessionManager.Enabled = true;
        EditorBehavior.Import(null);
        EditorBehavior.Update(d => d with { InsertSpaces = true, TabSize = 4 }, persist: false);

        var w = new MainWindow { Width = 1100, Height = 760 };
        try
        {
            w.Show(); Settle();
            var view = w.GetVisualDescendants().OfType<MainView>().Single();
            NoteDocument Open(string text, string name)
            {
                var d = (NoteDocument)typeof(MainView).GetMethod("AddDocument", flags)!.Invoke(view, [null, text, name, null])!;
                Settle(); d.Editor.Focus(); Settle(); return d;
            }
            var doc = Open("{", "data.json");
            var ed = (MarkdownTextBox)doc.Editor;
            Check(ed.EnableJsonAutoIndent, "A .json document enables JSON auto-indent");
            string T() => (ed.Text ?? "").Replace("\r\n", "\n");
            void Press(Key k, RawInputModifiers m = RawInputModifiers.None)
            {
                w.KeyPress(k, m, k == Key.Enter ? PhysicalKey.Enter : PhysicalKey.Enter, null);
                Settle();
            }
            void Type(string s) { ed.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = s }); Settle(); }
            void Set(string text, int caret) { ed.Text = text; Settle(); ed.CaretIndex = caret; Settle(); }

            Set("{", 1); Press(Key.Enter);
            Check(T() == "{\n    " && ed.CaretIndex == ed.Text!.Length, "Editor: Enter after { indents one level");
            ed.Undo(); Settle();
            Check(T() == "{", "A single undo reverts the indented Enter");

            Set("{}", 1); Press(Key.Enter);
            Check(T() == "{\n    \n}" && (ed.Text ?? "").Replace("\r\n", "\n")[..(ed.Text![..ed.CaretIndex].Replace("\r\n", "\n").Length)] == "{\n    ",
                $"Editor: Enter between {{}} opens an indented middle line ({T().Replace("\n","\\n")} @{ed.CaretIndex})");
            ed.Undo(); Settle();
            Check(T() == "{}", "Enter between braces is a single undo step");

            Set("{\n        ", 10); Type("}");
            Check(T() == "{\n}" && ed.CaretIndex == 3, $"Editor: typing }} on a blank indented line outdents to the opener ({T().Replace("\n","\\n")} @{ed.CaretIndex})");
            ed.Undo(); Settle();
            Check(T() == "{\n        ", "Outdenting a closer is a single undo step");

            Set("    \"a\": 1", 10); ed.Focus(); Settle(); Press(Key.Enter);
            Check(T() == "    \"a\": 1\n    " && ed.CaretIndex == ed.Text!.Length, $"Editor: Enter after a value keeps the line indent ({T().Replace("\n","\\n")} @{ed.CaretIndex})");

            EditorBehavior.Update(d => d with { InsertSpaces = true, TabSize = 2 }, persist: false);
            Set("{", 1); Press(Key.Enter);
            Check(T() == "{\n  ", "Editor: 2-space tab size indents by two spaces");
            EditorBehavior.Update(d => d with { InsertSpaces = false, TabSize = 4 }, persist: false);
            Set("{", 1); Press(Key.Enter);
            Check(T() == "{\n\t", "Editor: tab indent inserts a tab");
            EditorBehavior.Update(d => d with { InsertSpaces = true, TabSize = 4 }, persist: false);

            // Toggle off: today's behaviour (plain newline, closer types as-is).
            EditorBehavior.Update(d => d with { AutoIndentJson = false }, persist: false);
            Set("{", 1); Press(Key.Enter);
            Check(T() == "{\n", "Toggle off: Enter after { is a plain newline");
            Set("{}", 1); Press(Key.Enter);
            Check(T() == "{\n}", "Toggle off: Enter between {} is a plain newline");
            Set("{\n    ", 6); Type("}");
            Check(T() == "{\n    }", "Toggle off: } is inserted without outdenting");
            EditorBehavior.Update(d => d with { AutoIndentJson = true }, persist: false);

            // Non-.json files are unaffected even with the setting on.
            var md = Open("{", "note.md");
            var mdEd = (MarkdownTextBox)md.Editor;
            Check(!mdEd.EnableJsonAutoIndent, "A Markdown file does not enable JSON auto-indent");
            mdEd.CaretIndex = 1; Press(Key.Enter);
            Check((mdEd.Text ?? "").Replace("\r\n", "\n") == "{\n", "Markdown: Enter after { is a plain newline");
            var txt = Open("{", "plain.txt");
            var te = (MarkdownTextBox)txt.Editor;
            Check(!te.EnableJsonAutoIndent, "A .txt file does not enable JSON auto-indent");
            te.CaretIndex = 1; w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Settle();
            Check((te.Text ?? "").Replace("\r\n", "\n") == "{\n", "Plain text: Enter after { is a plain newline");
            var jsonc = Open("{", "data.jsonc");
            var jc = (MarkdownTextBox)jsonc.Editor;
            Check(!jc.EnableJsonAutoIndent, "A .jsonc file does not enable JSON auto-indent");
            jc.CaretIndex = 1; w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Settle();
            Check((jc.Text ?? "").Replace("\r\n", "\n") == "{\n", "jsonc: Enter after { is a plain newline");
            doc.Editor.Focus(); Settle();

            // CRLF is preserved by the editor's NewLine.
            var crlf = Open("{\r\n", "crlf.json");
            var ce = (MarkdownTextBox)crlf.Editor;
            Check(ce.NewLine == "\r\n", "A CRLF .json file keeps CRLF as NewLine");
            ce.CaretIndex = 1; w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Settle();
            Check(ce.Text == "{\r\n    \r\n", "Editor: Enter after { on a CRLF file writes CRLF");
            doc.Editor.Focus(); Settle();

            // ---- Gear + persistence ----
            var gear = w.GetVisualDescendants().OfType<Button>().First(b => b.Name == "EditorSettingsButton");
            var flyout = (Flyout)gear.Flyout!;
            flyout.ShowAt(gear); Settle();
            var content = (Control)flyout.Content!;
            CheckBox Box(string n) => content.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == n);
            Check(Box("EditorAutoIndentJsonBox").IsChecked == true && Box("EditorAutoIndentJsonBox").Content is TextBlock tb && tb.Text == Loc.T("EditorAutoIndentJson"),
                "The editor gear has the Auto indent JSON checkbox, on");
            Box("EditorAutoIndentJsonBox").IsChecked = false; Settle();
            flyout.Hide(); Settle();
            Check(!EditorBehavior.Current.AutoIndentJson, "Unticking the checkbox turns the setting off");
            var saved = SessionStore.Load(sessionPath)?.EditorBehavior;
            Check(saved is { AutoIndentJson: false }, "The switch is saved in the session");
            EditorBehavior.Import(null); EditorBehavior.Import(saved);
            Check(EditorBehavior.Current is { AutoIndentJson: false }, "Loading the session restores it");
            EditorBehavior.Update(_ => new EditorBehaviorData()); Settle();
            Check(SessionStore.Load(sessionPath)?.EditorBehavior is null, "All defaults are not written");

            Check(Loc.Languages.Where(l => l.Code != Loc.English).All(l => Loc.Table(l.Code).TryGetValue("EditorAutoIndentJson", out var v) && v.Length > 0 && v != Loc.En["EditorAutoIndentJson"]),
                $"The Auto indent JSON label is translated in all {Loc.Languages.Count} languages");

            foreach (var d in w.GetVisualDescendants().OfType<FileTabs>().Single().FindControl<ListBox>("Tabs")!.Items.OfType<NoteDocument>()) d.SavedText = d.Text;
        }
        finally
        {
            EditorBehavior.Import(null);
            w.Close();
            try { File.Delete(sessionPath); } catch { }
        }
        Console.WriteLine($"JSON indent tests: {passed} passed");
    }
}