namespace Mdhub.Services;

// The Open Recent list shared by every window: changes raise Changed (each window's File menu rebuilds)
// and are saved right away with the session file.
public static class RecentService
{
    public static IReadOnlyList<string> Files { get; private set; } = [];
    public static IReadOnlyList<string> Folders { get; private set; } = [];
    public static bool IsEmpty => Files.Count == 0 && Folders.Count == 0;
    public static event Action? Changed;

    public static void Load(SessionData? data)
    {
        Files = RecentList.Sanitize(data?.RecentFiles);
        Folders = RecentList.Sanitize(data?.RecentFolders);
        Changed?.Invoke();
    }

    public static void AddFile(string path) => Set(RecentList.Add(Files, path), Folders);
    public static void AddFolder(string path) => Set(Files, RecentList.Add(Folders, path));
    public static void Remove(string path) => Set(RecentList.Remove(Files, path), RecentList.Remove(Folders, path));
    public static void Clear() => Set([], []);

    private static void Set(IReadOnlyList<string> files, IReadOnlyList<string> folders)
    {
        if (files.SequenceEqual(Files) && folders.SequenceEqual(Folders)) return;
        Files = files;
        Folders = folders;
        SessionManager.SaveRecent();
        Changed?.Invoke();
    }
}