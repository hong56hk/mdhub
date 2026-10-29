using System.Diagnostics;

namespace Mdhub.Services;

public enum ExplorerAction { Reveal, Cut, Copy, Paste, Duplicate, Rename, Delete }
public sealed record ExplorerRequest(ExplorerAction Action, string Path);

public static class ExplorerFileOperations
{
    public static bool Contains(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar);
    }

    public static void Reveal(string path)
    {
        var start = new ProcessStartInfo { UseShellExecute = true };
        if (OperatingSystem.IsWindows()) { start.FileName = "explorer.exe"; start.Arguments = $"/select,\"{path}\""; }
        else if (OperatingSystem.IsMacOS()) { start.FileName = "open"; start.ArgumentList.Add("-R"); start.ArgumentList.Add(path); }
        else { start.FileName = "xdg-open"; start.ArgumentList.Add(Path.GetDirectoryName(path)!); }
        Process.Start(start);
    }

    public static string UniqueCopyPath(string path)
    {
        var parent = Path.GetDirectoryName(path)!;
        var extension = Directory.Exists(path) ? "" : Path.GetExtension(path);
        var name = Directory.Exists(path) ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        for (var i = 1; ; i++)
        {
            var target = Path.Combine(parent, $"{name} copy{(i == 1 ? "" : " " + i)}{extension}");
            if (!File.Exists(target) && !Directory.Exists(target)) return target;
        }
    }

    public static void Copy(string source, string destination)
    {
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("The destination already exists.");
        if (Directory.Exists(source) && Contains(source, destination)) throw new IOException("Cannot copy a folder into itself.");
        CheckLinks(source);
        CopyCore(source, destination);
    }
    private static void CheckLinks(string path)
    {
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Copying linked files or folders is not supported.");
        if (Directory.Exists(path)) foreach (var entry in Directory.EnumerateFileSystemEntries(path).Where(p => !FolderService.IsInternal(p))) CheckLinks(entry);
    }
    private static void CopyCore(string source, string destination)
    {
        if (!Directory.Exists(source)) { File.Copy(source, destination, false); return; }
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source).Where(p => !FolderService.IsInternal(p)))
            CopyCore(entry, Path.Combine(destination, Path.GetFileName(entry)));
    }
    public static void Move(string source, string destination)
    {
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("The destination already exists.");
        if (Directory.Exists(source))
        {
            if (Contains(source, destination)) throw new IOException("Cannot move a folder into itself.");
            Directory.Move(source, destination);
        }
        else File.Move(source, destination);
    }
    public static void Recycle(string root, string path)
    {
        if (!Contains(root, path) || Path.GetFullPath(root) == Path.GetFullPath(path)) throw new IOException("Select an item inside the opened folder.");
        if (Directory.Exists(path))
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        else
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
    }
    public static string Trash(string root, string path)
    {
        if (!Contains(root, path) || Path.GetFullPath(root) == Path.GetFullPath(path)) throw new IOException("Select an item inside the opened folder.");
        var trash = Path.Combine(root, ".md-trash", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(trash);
        var destination = Path.Combine(trash, Path.GetFileName(path));
        Move(path, destination);
        return destination;
    }
}
