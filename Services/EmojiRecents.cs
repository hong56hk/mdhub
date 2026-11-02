namespace Mdhub.Services;

// Most recently inserted first, shared by pickers and restored with the other app settings.
public static class EmojiRecents
{
    public const int MaxCount = 36;
    private static List<string> _values = [];
    public static IReadOnlyList<string> Values => _values.AsReadOnly();
    public static event Action? Changed;

    public static void Import(IEnumerable<string>? values)
    {
        var known = EmojiCatalog.Entries.Select(e => e.Value).ToHashSet(StringComparer.Ordinal);
        _values = (values ?? []).Where(known.Contains).Distinct(StringComparer.Ordinal).Take(MaxCount).ToList();
        Changed?.Invoke();
    }

    public static void Add(string emoji)
    {
        if (!EmojiCatalog.Entries.Any(e => e.Value == emoji)) return;
        _values.Remove(emoji);
        _values.Insert(0, emoji);
        if (_values.Count > MaxCount) _values.RemoveRange(MaxCount, _values.Count - MaxCount);
        Changed?.Invoke();
        SessionManager.SaveSettings();
    }
}
