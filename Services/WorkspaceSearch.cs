using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Mdhub.Text;

namespace Mdhub.Services;

public sealed record SearchHit(string Path, int Start, int Length, int Line, string Preview, int PreviewStart);
// Lossy: undecodable bytes were replaced with U+FFFD for searching, so the file must not be rewritten.
public sealed record SearchFile(string Path, string Source, Encoding Encoding, IReadOnlyList<SearchHit> Hits, bool Lossy = false);
public sealed record WorkspaceSearchResult(IReadOnlyList<SearchFile> Files, string? Error = null, int Skipped = 0, bool Limited = false, int Lossy = 0, bool Cancelled = false)
{
    public int Count => Files.Sum(f => f.Hits.Count);
}

public static class WorkspaceSearch
{
    public const int MaxResults = 5000;
    public const long MaxFileBytes = 5 * 1024 * 1024;
    private const int SniffBytes = 8000;

    // Item 46: one task walks the folder (skipping .git, internal files and .gitignore'd paths) and feeds a channel;
    // several workers read and match files in parallel. `found` is called (from a worker thread) for each file with hits
    // as soon as it is matched, so the panel can stream results; the returned result is sorted by path.
    // Cancellation returns a Cancelled result rather than throwing, so superseded searches stay quiet.
    public static Task<WorkspaceSearchResult> SearchAsync(string root, string query, FindOptions options,
        IReadOnlyDictionary<string, string> openBuffers, CancellationToken cancellationToken, Action<SearchFile>? found = null) => Task.Run(async () =>
    {
        var regex = FindReplace.Build(query, options, out var error);
        if (regex is null) return new WorkspaceSearchResult([], error);
        var cancelled = new WorkspaceSearchResult([], Cancelled: true);
        var files = new ConcurrentBag<SearchFile>();
        var skipped = 0;
        var lossyFiles = 0;
        var total = 0;
        string? timeout = null;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(1024) { SingleWriter = true });
        var walker = Task.Run(async () =>
        {
            try
            {
                var pending = new Stack<(DirectoryInfo Directory, GitIgnore Ignore)>();
                pending.Push((new DirectoryInfo(root), GitIgnore.Empty.Enter(root)));
                while (pending.TryPop(out var item))
                {
                    if (stop.IsCancellationRequested) return;
                    FileSystemInfo[] entries;
                    try { entries = item.Directory.GetFileSystemInfos(); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Interlocked.Increment(ref skipped); continue; }
                    Array.Sort(entries, (x, y) => StringComparer.OrdinalIgnoreCase.Compare(y.Name, x.Name));
                    foreach (var entry in entries)
                    {
                        var path = entry.FullName;
                        if (FolderService.IsInternal(path) || entry.Name == ".git") continue;
                        var attributes = entry.Attributes;
                        if (attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                        var isDirectory = attributes.HasFlag(FileAttributes.Directory);
                        // Open buffers are what the user is editing; search them even if ignored.
                        if (!isDirectory && openBuffers.ContainsKey(path)) { await channel.Writer.WriteAsync(path); continue; }
                        if (item.Ignore.IsIgnored(path, isDirectory)) continue;
                        if (isDirectory) pending.Push(((DirectoryInfo)entry, item.Ignore.Enter(path)));
                        else await channel.Writer.WriteAsync(path);
                    }
                }
            }
            finally { channel.Writer.TryComplete(); }
        });
        // No tokens are passed to the awaits: cancellation is polled, so a superseded search raises no exceptions.
        // Stopped workers keep draining the channel so the walker never blocks on a full channel.
        async Task Worker()
        {
            while (await channel.Reader.WaitToReadAsync())
                while (channel.Reader.TryRead(out var path))
                {
                    if (stop.IsCancellationRequested) continue;
                    try
                    {
                        string source;
                        Encoding encoding;
                        var lossy = false;
                        if (openBuffers.TryGetValue(path, out var buffer)) { source = buffer; encoding = TextFileStore.DefaultEncoding; }
                        else
                        {
                            // Not FileSupport.Detect: its strict sample decode rejects (and throws on) legacy-encoded text.
                            if (FileSupport.IsImage(path) || FileSupport.IsKnownBinary(path)) continue;
                            var info = new FileInfo(path);
                            if (info.Length > MaxFileBytes) { Interlocked.Increment(ref skipped); continue; }
                            var bytes = await File.ReadAllBytesAsync(path);
                            if (LooksBinary(bytes)) continue;
                            (source, encoding, lossy) = TextEncodings.DecodeForSearch(path, info, bytes); // item 67: same detection as opening, cached
                            if (!FileSupport.IsText(source)) continue;
                        }
                        // The caller's token, not `stop`: hits already counted towards MaxResults must be kept when another worker hits the limit.
                        var hits = Match(regex, path, source, ref total, cancellationToken);
                        if (hits.Count == 0) continue;
                        var file = new SearchFile(path, source, encoding, hits, lossy);
                        files.Add(file);
                        if (lossy) Interlocked.Increment(ref lossyFiles);
                        found?.Invoke(file);
                        if (Volatile.Read(ref total) >= MaxResults) stop.Cancel();
                    }
                    catch (RegexMatchTimeoutException) { timeout = Mdhub.Localization.Loc.T("SearchTimeout"); stop.Cancel(); }
                    // One unreadable or odd file never aborts the whole search.
                    catch (Exception) { Interlocked.Increment(ref skipped); }
                }
        }
        await Task.WhenAll(Enumerable.Range(0, Math.Max(2, Environment.ProcessorCount)).Select(_ => Task.Run(Worker)));
        await walker;
        if (cancellationToken.IsCancellationRequested) return cancelled;
        if (timeout is not null) return new WorkspaceSearchResult([], timeout);
        var sorted = files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        return new WorkspaceSearchResult(sorted, Skipped: skipped, Limited: Volatile.Read(ref total) >= MaxResults, Lossy: lossyFiles);
    }, CancellationToken.None);

    // A NUL byte in the first few KB means binary, unless a UTF-16/32 byte-order mark says otherwise.
    public static bool LooksBinary(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF)) return false;
        if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF) return false;
        if (TextFileStore.LooksUtf16(bytes)) return false; // item 67: BOM-less UTF-16
        return bytes[..Math.Min(bytes.Length, SniffBytes)].IndexOf((byte)0) >= 0;
    }

    private static List<SearchHit> Match(Regex regex, string path, string source, ref int total, CancellationToken token)
    {
        var hits = new List<SearchHit>();
        var line = 1;
        var scanned = 0;
        for (var match = regex.Match(source); match.Success; match = match.NextMatch())
        {
            if (token.IsCancellationRequested) break;
            if (match.Length == 0) continue;
            // Reserve a slot first so parallel workers never exceed MaxResults in total.
            if (Interlocked.Increment(ref total) > MaxResults) { Interlocked.Decrement(ref total); break; }
            while (scanned < match.Index)
            {
                if (source[scanned] == '\n' || source[scanned] == '\r' && (scanned + 1 == source.Length || source[scanned + 1] != '\n')) line++;
                scanned++;
            }
            var start = Math.Max(source.LastIndexOf('\n', Math.Max(0, match.Index - 1)), source.LastIndexOf('\r', Math.Max(0, match.Index - 1))) + 1;
            if (match.Index == 0) start = 0;
            start = Math.Max(start, match.Index - 60);
            var end = source.IndexOfAny(['\r', '\n'], match.Index);
            if (end < 0) end = source.Length;
            end = Math.Min(end, start + 180);
            hits.Add(new(path, match.Index, match.Length, line, source[start..end], start));
        }
        return hits;
    }
}