namespace Mdhub.Services;

public sealed record FolderEntry(string Path, bool IsDirectory, bool IsLink)
{
    public bool IsSupported { get; init; } = true;
}

public static class FolderService
{
    public static bool IsInternal(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals(".md-trash", StringComparison.OrdinalIgnoreCase)
            || (name.StartsWith(".mdhub-") && name.EndsWith(".tmp"));
    }

    public static string CreationDirectory(string? selectedPath, string? activeFilePath, string openedFolder)
    {
        var reference = selectedPath ?? activeFilePath;
        return reference is null ? openedFolder : Path.GetDirectoryName(reference) ?? openedFolder;
    }

    public static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Enter a name.";
        if (name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
            return "Enter a single file or folder name, without a path.";
        if (name.EndsWith(' ') || name.EndsWith('.')) return "Names cannot end with a space or period.";
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '0' and <= '9'))
            return "That name is reserved by Windows.";
        return null;
    }

    public static Task<string> CreateAsync(string directory, string name, bool isFolder) => Task.Run(() =>
    {
        var error = ValidateName(name);
        if (error is not null) throw new IOException(error);
        var path = Path.Combine(Path.GetFullPath(directory), name);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("The destination folder no longer exists.");
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("A file or folder with that name already exists.");
        if (isFolder) Directory.CreateDirectory(path);
        else { using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
        return path;
    });

    public static Task<FolderEntry[]> ReadAsync(string path) => Task.Run(() =>
        new DirectoryInfo(path).EnumerateFileSystemInfos()
            .Where(info => !IsInternal(info.FullName))
            .Select(info => new FolderEntry(info.FullName, info.Attributes.HasFlag(FileAttributes.Directory), info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                IsSupported = info.Attributes.HasFlag(FileAttributes.Directory) || FileSupport.Detect(info.FullName) != SupportedFileKind.Unsupported
            })
            .OrderByDescending(e => e.IsDirectory).ThenBy(e => Path.GetFileName(e.Path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => Path.GetFileName(e.Path), StringComparer.Ordinal).ToArray());
}
