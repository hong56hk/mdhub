using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mdhub.Services;

// One window as it was when it closed: folder, saved file tabs, the active tab and its bounds.
public sealed record WindowSession
{
    public string? Folder { get; init; }
    public List<string> Tabs { get; init; } = [];
    public int ActiveTab { get; init; } = -1;
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public string State { get; init; } = "Normal";
    public int Zoom { get; init; } = ZoomLevel.Default; // missing in old files: 100%
    [JsonIgnore] public bool IsEmpty => Folder is null && Tabs.Count == 0;
}

public sealed record SessionData
{
    public int Version { get; init; } = SessionStore.CurrentVersion;
    public List<WindowSession> Windows { get; init; } = [];
    public string Theme { get; init; } = "Dark"; // "Dark" or "Light"; missing in old files: Dark
    // UI language code ("en", "zh-Hant", "ja", "ko"); null until the user picks one (the OS language is used).
    public string? Language { get; init; }
    public List<string> RecentFiles { get; init; } = []; // File > Open Recent, newest first; missing in old files: empty
    public List<string> RecentFolders { get; init; } = [];
    // Custom shortcut bindings: id -> gestures like "Command+Shift+F"; missing in old files: none (defaults).
    public Dictionary<string, List<string>>? CustomShortcuts { get; init; }
    // Item 35: preview fonts and spacing; missing in old files (or all defaults): null.
    public PreviewSettingsData? Preview { get; init; }
    // Item 57: editor fonts (separate from the preview); null when both are the default.
    public EditorFontsData? EditorFonts { get; init; }
    // Item 62: editor colour overrides per theme (colour id -> #AARRGGBB); null when none.
    public EditorColorsData? EditorColors { get; init; }
    // Item 70: editor typing behaviour (tabs); null when all defaults.
    public EditorBehaviorData? EditorBehavior { get; init; }
    // Item 67: the Default encoding setting (TextEncodings id); null when UTF-8.
    public string? DefaultEncoding { get; init; }
    // Item 68: the Default line ending of new files (os, crlf, lf); null when the OS's.
    public string? DefaultLineEnding { get; init; }
}

// Item 70: Tab inserts spaces (default) or a tab; TabSize (1-8) = spaces per Tab and the width of a tab character.
public sealed record EditorBehaviorData
{
    public bool InsertSpaces { get; init; } = true;
    public int TabSize { get; init; } = 4;
    // Item 71: Markdown list / table auto-complete and table column alignment (all on by default).
    public bool AutoLists { get; init; } = true;
    public bool AutoTables { get; init; } = true;
    public bool AlignTables { get; init; } = true;
}

// UI-free session file handling: atomic save, tolerant load and validation against the file system.
// Item 57: the source editor's body and code font names (Consolas, the editor's font before item 57).
// Item 62: per-theme editor colour overrides, keyed by EditorColors item id.
public sealed record EditorColorsData
{
    public Dictionary<string, string>? Dark { get; init; }
    public Dictionary<string, string>? Light { get; init; }
}
public sealed record EditorFontsData
{
    public string Body { get; init; } = "Consolas";
    public string Code { get; init; } = "Consolas";
}

// Item 35: preview typography; null family = the app's UI font. Defaults match PreviewSettings.Default*.
public sealed record PreviewSettingsData
{
    public string? BodyFamily { get; init; }
    public double BodySize { get; init; } = 15;
    public string CodeFamily { get; init; } = "Consolas";
    public double CodeSize { get; init; } = 13;
    public double LineHeight { get; init; } = 1.6;
    public double ParagraphSpacing { get; init; } = 14;
    // Item 56: gap between list items (was the nested 8 px, i.e. 14 x 8/14).
    public double BulletSpacing { get; init; } = 0;
    public double OrderedSpacing { get; init; } = 8;
}

public static class SessionStore
{
    public const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mdhub", "session.json");

    public static string Serialize(SessionData data) => JsonSerializer.Serialize(data, Json);

    // Null for anything unusable: missing, unreadable, corrupt, or another schema version.
    public static SessionData? Deserialize(string json)
    {
        try
        {
            using var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object
                || !parsed.RootElement.TryGetProperty("Version", out var version) || version.ValueKind != JsonValueKind.Number
                || version.GetInt32() != CurrentVersion) return null;
            var data = parsed.RootElement.Deserialize<SessionData>(Json);
            if (data?.Windows is null) return null;
            return data with { Windows = data.Windows.Where(w => w is not null).Select(w => w with { Tabs = (w.Tabs ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() }).ToList() };
        }
        catch (Exception) { return null; }
    }

    public static SessionData? Load(string path)
    {
        try { return File.Exists(path) ? Deserialize(File.ReadAllText(path)) : null; }
        catch (Exception) { return null; }
    }

    // Write beside the destination, then replace it, so a failed write never leaves a truncated session.
    public static void Save(string path, SessionData data)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".session-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, Serialize(data));
            if (File.Exists(fullPath)) File.Replace(temporaryPath, fullPath, null);
            else File.Move(temporaryPath, fullPath);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    // Drops folders and files that no longer exist (counting them), keeps the active tab when it survives,
    // and drops windows left with nothing to restore.
    public static (List<WindowSession> Windows, int Skipped) Validate(SessionData? data, Func<string, bool> directoryExists, Func<string, bool> fileExists)
    {
        var windows = new List<WindowSession>();
        var skipped = 0;
        foreach (var window in data?.Windows ?? [])
        {
            var folder = window.Folder;
            if (folder is not null && !Exists(directoryExists, folder)) { folder = null; skipped++; }
            var activePath = window.ActiveTab >= 0 && window.ActiveTab < window.Tabs.Count ? window.Tabs[window.ActiveTab] : null;
            var tabs = new List<string>();
            foreach (var tab in window.Tabs)
            {
                if (Exists(fileExists, tab)) { if (!tabs.Contains(tab, StringComparer.OrdinalIgnoreCase)) tabs.Add(tab); }
                else skipped++;
            }
            var active = activePath is null ? -1 : tabs.FindIndex(t => string.Equals(t, activePath, StringComparison.OrdinalIgnoreCase));
            if (active < 0 && tabs.Count > 0) active = 0;
            var cleaned = window with { Folder = folder, Tabs = tabs, ActiveTab = active };
            if (!cleaned.IsEmpty) windows.Add(cleaned);
        }
        return (windows, skipped);
    }

    private static bool Exists(Func<string, bool> exists, string path)
    {
        try { return Path.IsPathFullyQualified(path) && exists(path); }
        catch (Exception) { return false; }
    }
}