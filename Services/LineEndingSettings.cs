using Mdhub.Text;

namespace Mdhub.Services;

// Item 68 setting: the line ending of new files (and of files without any line break): the OS's (CRLF on Windows,
// LF elsewhere), CRLF or LF. Saved in the session.
public static class LineEndingSettings
{
    public const string DefaultId = "os";
    public static readonly string[] Ids = ["os", "crlf", "lf"];
    public static string DefaultLineEndingId { get; private set; } = DefaultId;
    public static event Action? DefaultChanged;
    static string Valid(string? id) => Ids.FirstOrDefault(i => string.Equals(i, id, StringComparison.OrdinalIgnoreCase)) ?? DefaultId;
    public static void ImportDefault(string? id) => DefaultLineEndingId = Valid(id);
    public static void SetDefault(string id, bool persist = true)
    {
        var next = Valid(id);
        if (next == DefaultLineEndingId) return;
        DefaultLineEndingId = next;
        DefaultChanged?.Invoke();
        if (persist) SessionManager.SaveSettings();
    }
    public static LineEnding Resolve(string id) => id switch { "crlf" => LineEnding.CrLf, "lf" => LineEnding.Lf, _ => LineEndings.Os };
    public static LineEnding ForNewFile => Resolve(DefaultLineEndingId);
}