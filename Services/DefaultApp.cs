using System.Diagnostics;

namespace Mdhub.Services;

// Item 52: open a file with the operating system's default app.
public static class DefaultApp
{
    // Test seam: replaces the real launch (tests never start processes).
    public static Action<string>? LauncherOverride { get; set; }

    public static bool IsExecutable(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".exe" or ".bat" or ".cmd" or ".ps1" or ".app" or ".sh" or ".com" or ".msi" or ".scr" or ".vbs" or ".lnk";

    public static void Launch(string path)
    {
        if (LauncherOverride is not null) { LauncherOverride(path); return; }
        if (OperatingSystem.IsMacOS()) Process.Start(new ProcessStartInfo("open") { ArgumentList = { path }, UseShellExecute = false });
        else if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) ?? "" });
        else Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { path }, UseShellExecute = false });
    }
}