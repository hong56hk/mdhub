namespace Mdhub.Services;

public sealed record ExplorerImportRequest(IReadOnlyList<string> Sources, string Directory);

public static class ExplorerImport
{
    // External drops copy regular local files into the workspace. Resolve and
    // validate again when copying, since a drag can outlive a folder refresh.
    public static string? Destination(string root, string source, string directory)
    {
        try
        {
            root = Path.GetFullPath(root);
            source = Path.GetFullPath(source);
            directory = Path.GetFullPath(directory);
            if (!File.Exists(source) || !Directory.Exists(directory)
                || !ExplorerFileOperations.Contains(root, directory) || FolderService.IsInternal(source)
                || File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint)) return null;
            for (var current = directory; current is not null && ExplorerFileOperations.Contains(root, current); current = Path.GetDirectoryName(current))
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint) || FolderService.IsInternal(current)) return null;
            return Path.Combine(directory, Path.GetFileName(source));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }
}
