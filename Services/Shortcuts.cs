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
        ["OemPlus"] = "=", ["OemMinus"] = "-", ["OemPipe"] = "\\", ["Add"] = "Num +", ["Subtract"] = "Num -", ["NumPad0"] = "Num 0",
        ["D0"] = "0", ["OemQuotes"] = "'", ["OemSemicolon"] = ";", ["Escape"] = "Esc", ["Delete"] = "Del", ["Left"] = "Left", ["Right"] = "Right", ["Up"] = "Up", ["Down"] = "Down"
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
// Defaults are the built-in gestures; Gestures are the live ones (a user's custom binding when there is one).
// ExplorerOnly (item 53): acts only while the explorer has focus, so it may share a gesture with an Edit entry
// (Ctrl+C/X/V) without conflicting: the editor keeps its keys and the explorer gets them only when focused.
public sealed record Shortcut(string Id, ShortcutArea Area, string Label, IReadOnlyList<ShortcutGesture> Defaults, bool HandledByTextBox = false, bool ExplorerOnly = false)
{
    public IReadOnlyList<ShortcutGesture> Gestures => Shortcuts.GesturesFor(this);
    // Only mdhub's own handlers read the live table; gestures built into the TextBox cannot be rebound.
    public bool IsCustomizable => !HandledByTextBox;
    public bool IsCustomized => Shortcuts.IsCustomized(Id);
    // The label in the current UI language (the English Label is the fallback).
    public string DisplayLabel => Mdhub.Localization.Loc.T("Sc." + Id);
    public string Format(bool mac) => string.Join(" / ", Gestures.Where(g => !g.Hidden).Select(g => g.Format(mac)));
}

// The one shortcut table: menus (InputGesture), the key handlers and the Shortcut dialog all read it.
public static class Shortcuts
{
    private static Shortcut S(string id, ShortcutArea area, string label, params ShortcutGesture[] g) => new(id, area, label, g);
    private static Shortcut T(string id, ShortcutArea area, string label, params ShortcutGesture[] g) => new(id, area, label, g, true);
    private static Shortcut E(string id, string label, params ShortcutGesture[] g) => new(id, ShortcutArea.File, label, g, ExplorerOnly: true);
    private static ShortcutGesture C(string key) => ShortcutGesture.Cmd(key);
    private static ShortcutGesture CS(string key) => ShortcutGesture.CmdShift(key);
    private static ShortcutGesture P(string key) => ShortcutGesture.Plain(key);

    public static readonly IReadOnlyList<Shortcut> All =
    [
        S("file.new", ShortcutArea.File, "New note", C("N")),
        S("file.open", ShortcutArea.File, "Open file", C("O")),
        S("file.quickOpen", ShortcutArea.File, "Go to file", C("P")),
        S("file.openFolder", ShortcutArea.File, "Open folder", CS("O"), CS("N")),
        S("file.save", ShortcutArea.File, "Save", C("S")),
        S("file.saveAs", ShortcutArea.File, "Save as", CS("S")),
        S("file.closeTab", ShortcutArea.File, "Close tab", C("W")),
        S("explorer.rename", ShortcutArea.File, "Rename selected explorer item (explorer focused)", P("F2")),
        S("sidebar.explorer", ShortcutArea.View, "Show explorer", CS("E")),
        S("file.revealInSidebar", ShortcutArea.View, "Reveal active file in side bar", C("E")),
        S("sidebar.search", ShortcutArea.Find, "Search in folder", CS("F")),
        S("sidebar.replace", ShortcutArea.Find, "Replace in folder", CS("H")),
        T("edit.undo", ShortcutArea.Edit, "Undo", C("Z")),
        T("edit.redo", ShortcutArea.Edit, "Redo", C("Y"), CS("Z")),
        T("edit.cut", ShortcutArea.Edit, "Cut", C("X")),
        T("edit.copy", ShortcutArea.Edit, "Copy", C("C")),
        S("edit.paste", ShortcutArea.Edit, "Paste (text or image)", C("V")),
        T("edit.selectAll", ShortcutArea.Edit, "Select all", C("A")),
        // Item 72: line edits, editor focus only. Edit area, so Ctrl+D never conflicts with the explorer-only Duplicate.
        S("edit.moveLineUp", ShortcutArea.Edit, "Move line up", new ShortcutGesture("Up", ShortcutMods.Alt)),
        S("edit.moveLineDown", ShortcutArea.Edit, "Move line down", new ShortcutGesture("Down", ShortcutMods.Alt)),
        S("edit.duplicateLine", ShortcutArea.Edit, "Duplicate line", C("D")),
        // Item 53: after the Edit entries so a plain match of Ctrl+C/X/V still finds the editor entry first.
        E("explorer.cut", "Cut selected explorer item (explorer focused)", C("X")),
        E("explorer.copy", "Copy selected explorer item (explorer focused)", C("C")),
        E("explorer.paste", "Paste into the explorer selection (explorer focused)", C("V")),
        E("explorer.duplicate", "Duplicate selected explorer item (explorer focused)", C("D")),
        E("explorer.delete", "Move selected explorer item to the Recycle Bin (explorer focused)", P("Delete")),
        S("view.wordWrap", ShortcutArea.View, "Toggle word wrap", new ShortcutGesture("Z", ShortcutMods.Alt)),
        S("view.togglePreview", ShortcutArea.View, "Toggle preview", C("OemPipe"), new ShortcutGesture("OemBackslash", ShortcutMods.Command, Hidden: true)),
        S("find.find", ShortcutArea.Find, "Find", C("F")),
        S("find.replace", ShortcutArea.Find, "Find and replace", C("H")),
        S("find.next", ShortcutArea.Find, "Next match", P("F3")),
        S("find.previous", ShortcutArea.Find, "Previous match", new ShortcutGesture("F3", ShortcutMods.Shift)),
        S("find.close", ShortcutArea.Find, "Close find bar", P("Escape")),
        S("zoom.in", ShortcutArea.Zoom, "Zoom in", C("OemPlus"), CS("OemPlus"), C("Add")),
        S("zoom.out", ShortcutArea.Zoom, "Zoom out", C("OemMinus"), C("Subtract")),
        S("zoom.reset", ShortcutArea.Zoom, "Reset zoom", C("D0"), C("NumPad0")),
        // Item 49: VS Code tab cycling (wraps at both ends); handled at window level so the editor never inserts a tab.
        S("view.nextTab", ShortcutArea.View, "Next tab", C("Tab")),
        S("view.previousTab", ShortcutArea.View, "Previous tab", CS("Tab")),
        S("view.previewCaret", ShortcutArea.View, "Scroll preview to caret", CS("G")),
        S("editor.bold", ShortcutArea.Editor, "Bold (Markdown)", C("B")),
        S("editor.italic", ShortcutArea.Editor, "Italic (Markdown)", C("I")),
        S("editor.link", ShortcutArea.Editor, "Insert link (Markdown)", C("L")),
        // Ctrl+" is Shift+' on most layouts: accept both and show it as Ctrl+".
        S("editor.quote", ShortcutArea.Editor, "Block quote (Markdown)", new ShortcutGesture("OemQuotes", ShortcutMods.Command, "\""), new ShortcutGesture("OemQuotes", ShortcutMods.Command | ShortcutMods.Shift, Hidden: true)),
        S("editor.insertTime", ShortcutArea.Editor, "Insert date and time", C("OemSemicolon")),
        S("editor.goToLine", ShortcutArea.Editor, "Go to line", C("G")),
        // Item 74: next misspelling (Markdown files, spell checking on).
        S("editor.nextMisspelling", ShortcutArea.Editor, "Next misspelling", P("F7")),
        T("editor.wordLeft", ShortcutArea.Editor, "Move one word left", C("Left")),
        T("editor.wordRight", ShortcutArea.Editor, "Move one word right", C("Right")),
        T("editor.docStart", ShortcutArea.Editor, "Go to start of document", C("Home")),
        T("editor.docEnd", ShortcutArea.Editor, "Go to end of document", C("End")),
        T("editor.tab", ShortcutArea.Editor, "Insert tab", P("Tab")),
        T("editor.outdent", ShortcutArea.Editor, "Outdent", new ShortcutGesture("Tab", ShortcutMods.Shift)),
        S("editor.toggleOverwrite", ShortcutArea.Editor, "Toggle insert/overwrite", P("Insert")), // item 66
    ];

    // ---- Custom bindings (item 32). A custom entry replaces all of an entry's default gestures; an empty list
    // means "not assigned" (its gesture was reassigned to another entry).
    private static readonly Dictionary<string, List<ShortcutGesture>> Custom = [];
    public static event Action? Changed;

    internal static IReadOnlyList<ShortcutGesture> GesturesFor(Shortcut s) => Custom.TryGetValue(s.Id, out var list) ? list : s.Defaults;
    public static bool IsCustomized(string id) => Custom.ContainsKey(id);
    public static bool AnyCustomized => Custom.Count > 0;

    public static bool SameKeys(ShortcutGesture a, ShortcutGesture b) => a.Key == b.Key && a.Mods == b.Mods;

    // Every other entry whose live gestures include this key combination.
    public static IReadOnlyList<Shortcut> FindConflicts(string id, ShortcutGesture gesture) =>
        All.Where(s => s.Id != id && Overlap(Get(id), s) && s.Gestures.Any(g => SameKeys(g, gesture))).ToList();

    // Whether two entries can both fire for the same key press: explorer-only entries never meet the editor's Edit keys.
    public static bool Overlap(Shortcut a, Shortcut b) =>
        !(a.ExplorerOnly && b.Area == ShortcutArea.Edit || b.ExplorerOnly && a.Area == ShortcutArea.Edit);

    // Binds the gesture to the entry. With conflicts nothing changes unless reassign is set, and a gesture the
    // TextBox handles itself is never taken away. Returns false when nothing changed.
    public static bool Rebind(string id, ShortcutGesture gesture, bool reassign = false)
    {
        var target = Get(id);
        if (!target.IsCustomizable) return false;
        gesture = gesture with { KeyLabel = null, Hidden = false };
        var conflicts = FindConflicts(id, gesture);
        if (conflicts.Count > 0 && (!reassign || conflicts.Any(c => !c.IsCustomizable))) return false;
        foreach (var other in conflicts) Store(other, other.Gestures.Where(g => !SameKeys(g, gesture)).ToList());
        Store(target, [gesture]);
        Changed?.Invoke();
        return true;
    }

    // Keeps the table minimal: an entry equal to its defaults is not stored as custom.
    private static void Store(Shortcut s, List<ShortcutGesture> gestures)
    {
        if (gestures.Count == s.Defaults.Count && gestures.Zip(s.Defaults).All(p => SameKeys(p.First, p.Second))) Custom.Remove(s.Id);
        else Custom[s.Id] = gestures.Select(g => s.Defaults.FirstOrDefault(d => SameKeys(d, g)) is { Key: not null } d ? d : g).ToList();
    }

    public static void Reset(string id)
    {
        if (Custom.Remove(id)) Changed?.Invoke();
    }

    public static void ResetAll()
    {
        if (Custom.Count == 0) return;
        Custom.Clear();
        Changed?.Invoke();
    }

    // Persisted as id -> ["Command+Shift+F", ...] (modifier names, then the Avalonia Key name).
    public static string Serialize(ShortcutGesture g) =>
        string.Join("+", Enum.GetValues<ShortcutMods>().Where(m => m != ShortcutMods.None && g.Mods.HasFlag(m)).Select(m => m.ToString()).Append(g.Key));

    public static ShortcutGesture? Parse(string? text, Func<string, bool>? isKey = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('+');
        var mods = ShortcutMods.None;
        foreach (var part in parts[..^1])
        {
            if (!Enum.TryParse<ShortcutMods>(part, out var m) || m == ShortcutMods.None || !Enum.IsDefined(m)) return null;
            mods |= m;
        }
        var key = parts[^1];
        if (key.Length == 0 || (isKey is not null && !isKey(key))) return null;
        return new ShortcutGesture(key, mods);
    }

    public static Dictionary<string, List<string>> Export() => Custom.ToDictionary(p => p.Key, p => p.Value.Select(Serialize).ToList());

    // Replaces the custom table; unknown ids, rebinding a TextBox gesture and unparsable keys are ignored,
    // and a saved binding that would now clash with another entry is dropped (that entry keeps its default).
    public static void Import(IReadOnlyDictionary<string, List<string>>? saved, Func<string, bool>? isKey = null)
    {
        Custom.Clear();
        foreach (var (id, texts) in saved ?? new Dictionary<string, List<string>>())
        {
            if (All.FirstOrDefault(s => s.Id == id) is not { IsCustomizable: true } s || texts is null) continue;
            var gestures = texts.Select(t => Parse(t, isKey)).OfType<ShortcutGesture>().ToList();
            if (gestures.Count != texts.Count) continue;
            Store(s, gestures);
        }
        foreach (var id in Custom.Keys.ToList())
            if (Custom[id].Any(g => All.Any(o => o.Id != id && Overlap(Get(id), o) && o.Gestures.Any(x => SameKeys(x, g))))) Custom.Remove(id);
        Changed?.Invoke();
    }

    public static Shortcut Get(string id) => All.First(s => s.Id == id);
    public static string Format(string id, bool mac) => Get(id).Format(mac);

    // Pairs of entries that share a gesture (should be empty).
    public static IEnumerable<(Shortcut A, Shortcut B, ShortcutGesture Gesture)> Conflicts() =>
        All.SelectMany((s, i) => All.Skip(i + 1).Where(o => Overlap(s, o))
           .SelectMany(o => s.Gestures.Where(g => o.Gestures.Any(x => SameKeys(x, g))).Select(g => (s, o, g))));
}
