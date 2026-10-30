using Avalonia.Media;

namespace Mdhub.Services;

// Item 57: the source editor's fonts (gear at the editor's top-right). Body = normal text, Code = code spans and
// fenced code. Separate from the preview's fonts; saved in the session; applies live to every open editor.
public static class EditorFonts
{
    public const string DefaultFamily = "Consolas";
    public const string Fallback = "Consolas, Menlo, monospace";

    public static EditorFontsData Current { get; private set; } = new();
    public static event Action? Changed;
    public static bool IsDefault => Current == new EditorFontsData();

    private static string Clean(string? name) => string.IsNullOrWhiteSpace(name) ? DefaultFamily : name.Trim();
    private static EditorFontsData Normalize(EditorFontsData? data) => new() { Body = Clean(data?.Body), Code = Clean(data?.Code) };

    // Loading a session: no event, no save.
    public static void Import(EditorFontsData? data) => Current = Normalize(data);

    public static void Update(Func<EditorFontsData, EditorFontsData> change, bool persist = true)
    {
        var next = Normalize(change(Current));
        if (next == Current) return;
        Current = next;
        Changed?.Invoke();
        if (persist) SessionManager.SaveSettings();
    }

    // One FontFamily instance per name, so layouts can compare families cheaply.
    private static readonly Dictionary<string, FontFamily> Families = new(StringComparer.OrdinalIgnoreCase);
    public static FontFamily Family(string name)
    {
        if (!Families.TryGetValue(name, out var family))
            Families[name] = family = new FontFamily(string.Equals(name, DefaultFamily, StringComparison.OrdinalIgnoreCase) ? Fallback : name + ", " + Fallback);
        return family;
    }
    public static FontFamily BodyFamily => Family(Current.Body);
    public static FontFamily CodeFamily => Family(Current.Code);
    public static bool SeparateCodeFont => !string.Equals(Current.Body, Current.Code, StringComparison.OrdinalIgnoreCase);
}
