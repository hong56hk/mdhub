namespace Mdhub.Services;

public sealed record ExplorerMoveRequest(string Source, string Directory);

public static class ExplorerMove
{
    // Null means this is not a valid move target. Never overwrite another file.
    public static string? Destination(string root, string source, string directory)
    {
        try
        {
            root = Path.GetFullPath(root);
            source = Path.GetFullPath(source);
            directory = Path.GetFullPath(directory);
            if (!File.Exists(source) || !Directory.Exists(directory)
                || !ExplorerFileOperations.Contains(root, source) || !ExplorerFileOperations.Contains(root, directory)
                || FolderService.IsInternal(source) || File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint)) return null;
            for (var current = directory; current is not null && ExplorerFileOperations.Contains(root, current); current = Path.GetDirectoryName(current))
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) return null;
            var destination = Path.Combine(directory, Path.GetFileName(source));
            if (File.Exists(destination) || Directory.Exists(destination)) return null;
            return destination;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }
}
