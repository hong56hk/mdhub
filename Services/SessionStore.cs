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
}

// UI-free session file handling: atomic save, tolerant load and validation against the file system.
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