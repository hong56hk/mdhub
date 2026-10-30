using System.Reflection;

namespace Mdhub.Services;

// Item 58: what the About dialog shows. The version comes from the assembly's informational version (Mdhub.csproj),
// without any "+commit" source-revision suffix; the LICENSE file at the repo root is embedded so it opens offline.
public static class AppInfo
{
    public const string GitHubDisplay = "github.com/hong56hk/mdhub";
    public static readonly Uri GitHubUri = new("https://github.com/hong56hk/mdhub");
    public const string LicenseResource = "Mdhub.LICENSE";
    public const string LicenseTabName = "LICENSE";

    public static string Version { get; } = CleanVersion(typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    public static string CleanVersion(string? informational)
    {
        var version = informational ?? "";
        var plus = version.IndexOf('+');
        return (plus >= 0 ? version[..plus] : version).Trim();
    }

    public static string LicenseText()
    {
        using var stream = typeof(AppInfo).Assembly.GetManifestResourceStream(LicenseResource) ?? throw new FileNotFoundException(null, LicenseResource);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }
}
