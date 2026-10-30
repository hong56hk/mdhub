using System.Text;
using System.Text.RegularExpressions;
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
    public static Task<WorkspaceSearchResult> SearchAsync(string root, string query, FindOptions options,
        IReadOnlyDictionary<string, string> openBuffers, CancellationToken cancellationToken) => Task.Run(async () =>
    // Cancellation returns a Cancelled result rather than throwing, so superseded searches stay quiet.
    {
        var regex = FindReplace.Build(query, options, out var error);
        if (regex is null) return new WorkspaceSearchResult([], error);
        var files = new List<SearchFile>();
        var skipped = 0;
        var lossyFiles = 0;
        var cancelled = new WorkspaceSearchResult([], Cancelled: true);
        var total = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            if (cancellationToken.IsCancellationRequested) return cancelled;
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(directory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; continue; }
            foreach (var path in entries.Order(StringComparer.OrdinalIgnoreCase))
            {
                if (cancellationToken.IsCancellationRequested) return cancelled;
                if (FolderService.IsInternal(path) || Path.GetFileName(path) == ".git") continue;
                try
                {
                    var attributes = File.GetAttributes(path);
                    if (attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    if (attributes.HasFlag(FileAttributes.Directory)) { pending.Push(path); continue; }
                    string source;
                    Encoding encoding;
                    var lossy = false;
                    if (openBuffers.TryGetValue(path, out var buffer)) { source = buffer; encoding = TextFileStore.DefaultEncoding; }
                    else
                    {
                        // Not FileSupport.Detect: its strict sample decode rejects (and throws on) legacy-encoded text.
                        if (FileSupport.IsImage(path) || FileSupport.IsKnownBinary(path)) continue;
                        if (new FileInfo(path).Length > MaxFileBytes) { skipped++; continue; }
                        (source, encoding, lossy) = await TextFileStore.ReadLenientAsync(path);
                        if (cancellationToken.IsCancellationRequested) return cancelled;
                        if (!FileSupport.IsText(source)) continue;
                    }
                    var hits = new List<SearchHit>();
                    var line = 1;
                    var scanned = 0;
                    for (var match = regex.Match(source); match.Success; match = match.NextMatch())
                    {
                        if (cancellationToken.IsCancellationRequested) return cancelled;
                        if (match.Length == 0) continue;
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
                        total++;
                        if (total >= MaxResults) break;
                    }
                    if (hits.Count > 0) { files.Add(new(path, source, encoding, hits, lossy)); if (lossy) lossyFiles++; }
                    if (total >= MaxResults) return new WorkspaceSearchResult(files, Skipped: skipped, Limited: true, Lossy: lossyFiles);
                }
                catch (RegexMatchTimeoutException) { return new WorkspaceSearchResult([], Mdhub.Localization.Loc.T("SearchTimeout")); }
                // One unreadable or odd file never aborts the whole search.
                catch (Exception) { skipped++; }
            }
        }
        return new WorkspaceSearchResult(files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToArray(), Skipped: skipped, Lossy: lossyFiles);
    }, CancellationToken.None);
}
