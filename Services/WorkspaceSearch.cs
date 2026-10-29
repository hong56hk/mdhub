using System.Text;
using System.Text.RegularExpressions;
using Mdhub.Text;

namespace Mdhub.Services;

public sealed record SearchHit(string Path, int Start, int Length, int Line, string Preview, int PreviewStart);
public sealed record SearchFile(string Path, string Source, Encoding Encoding, IReadOnlyList<SearchHit> Hits);
public sealed record WorkspaceSearchResult(IReadOnlyList<SearchFile> Files, string? Error = null, int Skipped = 0, bool Limited = false)
{
    public int Count => Files.Sum(f => f.Hits.Count);
}

public static class WorkspaceSearch
{
    public const int MaxResults = 5000;
    public const long MaxFileBytes = 5 * 1024 * 1024;
    public static Task<WorkspaceSearchResult> SearchAsync(string root, string query, FindOptions options,
        IReadOnlyDictionary<string, string> openBuffers, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        var regex = FindReplace.Build(query, options, out var error);
        if (regex is null) return new WorkspaceSearchResult([], error);
        var files = new List<SearchFile>();
        var skipped = 0;
        var total = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(directory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; continue; }
            foreach (var path in entries.Order(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (FolderService.IsInternal(path) || Path.GetFileName(path) == ".git") continue;
                try
                {
                    var attributes = File.GetAttributes(path);
                    if (attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    if (attributes.HasFlag(FileAttributes.Directory)) { pending.Push(path); continue; }
                    string source;
                    Encoding encoding;
                    if (openBuffers.TryGetValue(path, out var buffer)) { source = buffer; encoding = TextFileStore.DefaultEncoding; }
                    else
                    {
                        if (FileSupport.Detect(path) != SupportedFileKind.Text) continue;
                        if (new FileInfo(path).Length > MaxFileBytes) { skipped++; continue; }
                        (source, encoding) = await TextFileStore.ReadAsync(path);
                        if (!FileSupport.IsText(source)) continue;
                    }
                    var hits = new List<SearchHit>();
                    var line = 1;
                    var scanned = 0;
                    for (var match = regex.Match(source); match.Success; match = match.NextMatch())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
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
                    if (hits.Count > 0) files.Add(new(path, source, encoding, hits));
                    if (total >= MaxResults) return new WorkspaceSearchResult(files, Skipped: skipped, Limited: true);
                }
                catch (RegexMatchTimeoutException) { return new WorkspaceSearchResult([], "Search pattern took too long. Try a simpler expression."); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException) { skipped++; }
            }
        }
        return new WorkspaceSearchResult(files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToArray(), Skipped: skipped);
    }, cancellationToken);
}
