namespace Mdhub.Services;

public enum ShortcutArea { File, Edit, View, Find, Zoom, Editor }

// Command is Ctrl on Windows/Linux and Cmd on macOS.
[Flags] public enum ShortcutMods { None = 0, Command = 1, Shift = 2, Alt = 4 }

// Key is an Avalonia Key enum name (kept as a string so this file stays UI-free).
// KeyLabel overrides the key name for display; Hidden gestures work but are not shown.
public readonly record struct ShortcutGesture(string Key, ShortcutMods Mods, string? KeyLabel = null, bool Hidden = false)
{
    private static readonly Dictionary<string, string> KeyNames = new()
    {
        ["OemPlus"] = "=", ["OemMinus"] = "-", ["Add"] = "Num +", ["Subtract"] = "Num -", ["NumPad0"] = "Num 0",
        ["D0"] = "0", ["OemQuotes"] = "'", ["OemSemicolon"] = ";", ["Escape"] = "Esc", ["Left"] = "Left", ["Right"] = "Right"
    };

    public string Format(bool mac)
    {
        var parts = new List<string>();
        if (Mods.HasFlag(ShortcutMods.Command)) parts.Add(mac ? "Cmd" : "Ctrl");
        if (Mods.HasFlag(ShortcutMods.Alt)) parts.Add(mac ? "Option" : "Alt");
        if (Mods.HasFlag(ShortcutMods.Shift)) parts.Add("Shift");
        parts.Add(KeyLabel ?? (KeyNames.TryGetValue(Key, out var name) ? name : Key));
        return string.Join("+", parts);
    }

    public static ShortcutGesture Cmd(string key) => new(key, ShortcutMods.Command);
    public static ShortcutGesture CmdShift(string key) => new(key, ShortcutMods.Command | ShortcutMods.Shift);
    public static ShortcutGesture Plain(string key) => new(key, ShortcutMods.None);
}

// Handled says who acts on it: App (mdhub's key handlers) or TextBox (built into the editor, listed for reference).
public sealed record Shortcut(string Id, ShortcutArea Area, string Label, IReadOnlyList<ShortcutGesture> Gestures, bool HandledByTextBox = false)
{
    public string Format(bool mac) => string.Join(" / ", Gestures.Where(g => !g.Hidden).Select(g => g.Format(mac)));
}

// The one shortcut table: menus (InputGesture), the key handlers and the Shortcut dialog all read it.
public static class Shortcuts
{
    private static Shortcut S(string id, ShortcutArea area, string label, params ShortcutGesture[] g) => new(id, area, label, g);
    private static Shortcut T(string id, ShortcutArea area, string label, params ShortcutGesture[] g) => new(id, area, label, g, true);
    private static ShortcutGesture C(string key) => ShortcutGesture.Cmd(key);
    private static ShortcutGesture CS(string key) => ShortcutGesture.CmdShift(key);
    private static ShortcutGesture P(string key) => ShortcutGesture.Plain(key);

    public static readonly IReadOnlyList<Shortcut> All =
    [
        S("file.new", ShortcutArea.File, "New note", C("N")),
        S("file.open", ShortcutArea.File, "Open file", C("O")),
        S("file.openFolder", ShortcutArea.File, "Open folder", CS("O")),
        S("file.save", ShortcutArea.File, "Save", C("S")),
        S("file.saveAs", ShortcutArea.File, "Save as", CS("S")),
        S("file.closeTab", ShortcutArea.File, "Close tab", C("W")),
        T("edit.undo", ShortcutArea.Edit, "Undo", C("Z")),
        T("edit.redo", ShortcutArea.Edit, "Redo", C("Y"), CS("Z")),
        T("edit.cut", ShortcutArea.Edit, "Cut", C("X")),
        T("edit.copy", ShortcutArea.Edit, "Copy", C("C")),
        S("edit.paste", ShortcutArea.Edit, "Paste (text or image)", C("V")),
        T("edit.selectAll", ShortcutArea.Edit, "Select all", C("A")),
        S("view.wordWrap", ShortcutArea.View, "Toggle word wrap", new ShortcutGesture("Z", ShortcutMods.Alt)),
        S("find.find", ShortcutArea.Find, "Find", C("F")),
        S("find.replace", ShortcutArea.Find, "Find and replace", C("H")),
        S("find.next", ShortcutArea.Find, "Next match", P("F3")),
        S("find.previous", ShortcutArea.Find, "Previous match", new ShortcutGesture("F3", ShortcutMods.Shift)),
        S("find.close", ShortcutArea.Find, "Close find bar", P("Escape")),
        S("zoom.in", ShortcutArea.Zoom, "Zoom in", C("OemPlus"), CS("OemPlus"), C("Add")),
        S("zoom.out", ShortcutArea.Zoom, "Zoom out", C("OemMinus"), C("Subtract")),
        S("zoom.reset", ShortcutArea.Zoom, "Reset zoom", C("D0"), C("NumPad0")),
        S("view.previewCaret", ShortcutArea.View, "Scroll preview to caret", C("P")),
        S("editor.bold", ShortcutArea.Editor, "Bold (Markdown)", C("B")),
        S("editor.italic", ShortcutArea.Editor, "Italic (Markdown)", C("I")),
        S("editor.link", ShortcutArea.Editor, "Insert link (Markdown)", C("L")),
        // Ctrl+" is Shift+' on most layouts: accept both and show it as Ctrl+".
        S("editor.quote", ShortcutArea.Editor, "Block quote (Markdown)", new ShortcutGesture("OemQuotes", ShortcutMods.Command, "\""), new ShortcutGesture("OemQuotes", ShortcutMods.Command | ShortcutMods.Shift, Hidden: true)),
        S("editor.insertTime", ShortcutArea.Editor, "Insert date and time", C("OemSemicolon")),
        S("editor.goToLine", ShortcutArea.Editor, "Go to line", C("G")),
        T("editor.wordLeft", ShortcutArea.Editor, "Move one word left", C("Left")),
        T("editor.wordRight", ShortcutArea.Editor, "Move one word right", C("Right")),
        T("editor.docStart", ShortcutArea.Editor, "Go to start of document", C("Home")),
        T("editor.docEnd", ShortcutArea.Editor, "Go to end of document", C("End")),
        T("editor.tab", ShortcutArea.Editor, "Insert tab", P("Tab")),
    ];

    public static Shortcut Get(string id) => All.First(s => s.Id == id);
    public static string Format(string id, bool mac) => Get(id).Format(mac);

    // Pairs of entries that share a gesture (should be empty).
    public static IEnumerable<(Shortcut A, Shortcut B, ShortcutGesture Gesture)> Conflicts() =>
        All.SelectMany(s => s.Gestures.Select(g => (s, g))).GroupBy(x => (x.g.Key, x.g.Mods)).Where(grp => grp.Count() > 1)
           .SelectMany(grp => grp.Skip(1).Select(x => (grp.First().s, x.s, x.g)));
}