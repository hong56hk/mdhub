using Mdhub.Text;

namespace Mdhub.Services;

// Item 70: editor typing behaviour from the editor gear (Tab inserts spaces, tab size); saved in the session,
// applies live to every open editor.
public static class EditorBehavior
{
    public static EditorBehaviorData Current { get; private set; } = new();
    public static event Action? Changed;
    public static bool IsDefault => Current == new EditorBehaviorData();

    public static EditorBehaviorData Normalize(EditorBehaviorData? data) => (data ?? new()) with
    {
        TabSize = TabEditing.Clamp((data ?? new()).TabSize),
        // Item 74: an unknown dictionary falls back to the default (null).
        SpellLanguage = SpellCheck.Normalize(data?.SpellLanguage),
    };

    // Loading a session: no event, no save.
    public static void Import(EditorBehaviorData? data) => Current = Normalize(data);

    public static void Update(Func<EditorBehaviorData, EditorBehaviorData> change, bool persist = true)
    {
        var next = Normalize(change(Current));
        if (next == Current) return;
        Current = next;
        Changed?.Invoke();
        if (persist) SessionManager.SaveSettings();
    }
}
