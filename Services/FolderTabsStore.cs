using System.Text.Json;

namespace Mdhub.Services;

// Item 64: the open tabs of each folder, restored when the folder is opened again (like VS Code).
// Lives in its own file beside session.json; nothing is ever written inside the user's folder.
public sealed record FolderTabs
{
    public List<string> Tabs { get; init; } = [];
    public int ActiveTab { get; init; } = -1;
    public DateTime LastUsed { get; init; }
}

public sealed record FolderTabsData
{
    public int Version { get; init; } = FolderTabsStore.CurrentVersion;
    // Keyed by FolderTabsStore.Key: full path, no trailing separator, lower-case on Windows.
    public Dictionary<string, FolderTabs> Folders { get; init; } = [];
}

public static class FolderTabsStore
{
    public const int CurrentVersion = 1;
    public const int Capacity = 50; // folders kept; the least recently used go first
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    // session.json -> session-folder-tabs.json in the same app data folder.
    public static string PathFor(string sessionPath)
        => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sessionPath))!, Path.GetFileNameWithoutExtension(sessionPath) + "-folder-tabs.json");

    public static string Key(string folder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        return OperatingSystem.IsWindows() ? full.ToLowerInvariant() : full;
    }

    // Records (or replaces) a folder's tabs as the most recent entry and trims the history to Capacity.
    public static FolderTabsData Remember(FolderTabsData? data, string folder, IReadOnlyList<string> tabs, int activeTab, DateTime now)
    {
        var folders = new Dictionary<string, FolderTabs>(data?.Folders ?? []);
        var list = new List<string>();
        foreach (var tab in tabs) if (!list.Contains(tab, StringComparer.OrdinalIgnoreCase)) list.Add(tab);
        var activePath = activeTab >= 0 && activeTab < tabs.Count ? tabs[activeTab] : null;
        folders[Key(folder)] = new FolderTabs { Tabs = list, ActiveTab = activePath is null ? -1 : list.FindIndex(t => string.Equals(t, activePath, StringComparison.OrdinalIgnoreCase)), LastUsed = now };
        foreach (var old in folders.OrderByDescending(f => f.Value.LastUsed).Skip(Capacity).Select(f => f.Key).ToList()) folders.Remove(old);
        return new FolderTabsData { Folders = folders };
    }

    // The folder's saved tabs that still exist, with the active one kept when it survives (else the first).
    public static FolderTabs? Recall(FolderTabsData? data, string folder, Func<string, bool> fileExists)
    {
        if (data is null || !data.Folders.TryGetValue(Key(folder), out var saved) || saved is null) return null;
        var activePath = saved.ActiveTab >= 0 && saved.ActiveTab < saved.Tabs.Count ? saved.Tabs[saved.ActiveTab] : null;
        var tabs = (saved.Tabs ?? []).Where(t => Exists(fileExists, t)).ToList();
        var active = activePath is null ? -1 : tabs.FindIndex(t => string.Equals(t, activePath, StringComparison.OrdinalIgnoreCase));
        if (active < 0 && tabs.Count > 0) active = 0;
        return saved with { Tabs = tabs, ActiveTab = active };
    }

    public static string Serialize(FolderTabsData data) => JsonSerializer.Serialize(data, Json);

    public static FolderTabsData? Deserialize(string json)
    {
        try
        {
            var data = JsonSerializer.Deserialize<FolderTabsData>(json, Json);
            if (data is null || data.Version != CurrentVersion || data.Folders is null) return null;
            return data with { Folders = data.Folders.Where(f => f.Value is not null).ToDictionary(f => f.Key, f => f.Value with { Tabs = (f.Value.Tabs ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() }) };
        }
        catch (Exception) { return null; }
    }

    public static FolderTabsData? Load(string path)
    {
        try { return File.Exists(path) ? Deserialize(File.ReadAllText(path)) : null; }
        catch (Exception) { return null; }
    }

    // Atomic like session.json: write beside the destination, then replace it.
    public static void Save(string path, FolderTabsData data)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".folder-tabs-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, Serialize(data));
            if (File.Exists(fullPath)) File.Replace(temporaryPath, fullPath, null);
            else File.Move(temporaryPath, fullPath);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    // App hook: remember a window's folder and tabs; never throws.
    public static void Record(string storePath, WindowSession session)
    {
        if (session.Folder is not { } folder) return;
        try { Save(storePath, Remember(Load(storePath), folder, session.Tabs, session.ActiveTab, DateTime.UtcNow)); }
        catch (Exception) { /* Tab history must never block closing. */ }
    }

    private static bool Exists(Func<string, bool> exists, string path)
    {
        try { return Path.IsPathFullyQualified(path) && exists(path); }
        catch (Exception) { return false; }
    }
}
