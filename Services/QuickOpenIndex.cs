namespace Mdhub.Services;

// Every file under a folder, recursively and with no exclusions (hidden folders, bin/obj and gitignored paths are
// all listed), as relative paths. Built off the UI thread and cached; a FileSystemWatcher marks the cache stale and
// raises Changed so an open Quick Open box can re-query. Linked folders are listed but not followed (no cycles).
public sealed class QuickOpenIndex : IDisposable
{
    public const int MaxFiles = 500_000;
    private readonly object _gate = new();
    private Task<IReadOnlyList<string>>? _build;
    private FileSystemWatcher? _watcher;
    private int _version;
    public string Root { get; }
    public event Action? Changed;
    public bool Truncated { get; private set; }

    public QuickOpenIndex(string root, bool watch = true)
    {
        Root = Path.GetFullPath(root);
        if (!watch) return;
        try
        {
            _watcher = new FileSystemWatcher(Root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
            _watcher.Created += (_, _) => Invalidate();
            _watcher.Deleted += (_, _) => Invalidate();
            _watcher.Renamed += (_, _) => Invalidate();
            _watcher.Error += (_, _) => Invalidate();
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception) { _watcher?.Dispose(); _watcher = null; }
    }

    public int Version => _version;

    public void Invalidate()
    {
        lock (_gate) { _build = null; _version++; }
        Changed?.Invoke();
    }

    // The cached list, or a new background build when stale. Concurrent callers share one build.
    public Task<IReadOnlyList<string>> GetFilesAsync()
    {
        lock (_gate)
        {
            if (_build is { IsFaulted: false, IsCanceled: false } build) return build;
            var task = Task.Run(() => { var list = Enumerate(Root, MaxFiles, out var truncated); Truncated = truncated; return list; });
            _build = task;
            return task;
        }
    }

    public static IReadOnlyList<string> Enumerate(string root, int max, out bool truncated)
    {
        truncated = false;
        var files = new List<string>();
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false, ReturnSpecialDirectories = false };
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            IEnumerable<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", options).ToList(); }
            catch (Exception) { continue; }
            foreach (var entry in entries)
            {
                if (entry is DirectoryInfo)
                {
                    if (!entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) pending.Push(entry.FullName);
                    continue;
                }
                if (files.Count >= max) { truncated = true; return files; }
                files.Add(Path.GetRelativePath(root, entry.FullName));
            }
        }
        return files;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }
}