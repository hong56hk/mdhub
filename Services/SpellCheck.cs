using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mdhub.Text;
using WeCantSpell.Hunspell;

namespace Mdhub.Services;

public sealed record SpellLanguage(string Code, string Name);

// Item 74: Hunspell spell checking (WeCantSpell.Hunspell, used unmodified under its MPL option) with the bundled
// en_US, fr and es_ES dictionaries (see THIRD-PARTY-NOTICES.md). Custom words live in appdata; "Ignore in this file"
// is kept per editor for the session. Dictionaries load lazily on first use (off the UI thread when the overlay asks).
public static class SpellCheck
{
    public static readonly IReadOnlyList<SpellLanguage> Languages =
    [
        new("en_US", "English (US)"),
        new("fr", "Fran\u00e7ais"),
        new("es_ES", "Espa\u00f1ol (Espa\u00f1a)"),
    ];

    // Dev builds and the macOS app copy Dictionaries next to the binary. The single-file Windows exe embeds them
    // (see Mdhub.csproj) and the first use extracts them under local app data.
    public static string DictionaryFolder { get; set; } = ResolveDictionaryFolder();
    const string BundlePrefix = "mdhub.bundle.dictionaries.";
    const string BundleNotices = "mdhub.bundle.THIRD-PARTY-NOTICES.md";

    static string ResolveDictionaryFolder()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "Dictionaries");
        if (File.Exists(Path.Combine(beside, "en_US.dic")) && File.Exists(Path.Combine(beside, "en_US.aff"))) return beside;
        return ExtractBundledDictionaries() ?? beside;
    }

    static string? ExtractBundledDictionaries()
    {
        try
        {
            var assembly = typeof(SpellCheck).Assembly;
            var names = assembly.GetManifestResourceNames().Where(n => n.StartsWith(BundlePrefix, StringComparison.Ordinal)).ToList();
            if (names.Count == 0) return null;
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(root)) return null;
            var version = AppInfo.Version;
            if (string.IsNullOrWhiteSpace(version)) version = "unknown";
            var dest = Path.Combine(root, "mdhub", "bundled-dictionaries", version);
            Directory.CreateDirectory(dest);
            foreach (var name in names)
            {
                var fileName = name[BundlePrefix.Length..];
                if (fileName is "." or ".." || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
                ExtractResource(assembly, name, Path.Combine(dest, fileName));
            }
            ExtractResource(assembly, BundleNotices, Path.Combine(dest, "THIRD-PARTY-NOTICES.md"));
            return File.Exists(Path.Combine(dest, "en_US.dic")) && File.Exists(Path.Combine(dest, "en_US.aff")) ? dest : null;
        }
        catch (Exception) { return null; }
    }

    static void ExtractResource(Assembly assembly, string name, string path)
    {
        using var source = assembly.GetManifestResourceStream(name);
        if (source is null) return;
        if (source.CanSeek && File.Exists(path) && new FileInfo(path).Length == source.Length) return;
        var partial = path + ".partial";
        using (var output = File.Create(partial)) source.CopyTo(output);
        File.Move(partial, path, true);
    }

    public static string CustomWordsPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mdhub", "dictionary.txt");
    // Custom words or ignores changed: open editors re-check.
    public static event Action? Changed;

    public static bool Enabled => EditorBehavior.Current.SpellCheck;
    public static string? Normalize(string? code) => Languages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))?.Code;
    // The UI language's dictionary, or English when there is none (de, zh, ja, ko).
    public static string DefaultFor(string uiLanguage) => uiLanguage switch { "fr" => "fr", "es" => "es_ES", _ => "en_US" };
    public static string ActiveLanguage => Normalize(EditorBehavior.Current.SpellLanguage) ?? DefaultFor(Mdhub.Localization.Loc.Current);

    private static readonly ConcurrentDictionary<string, Lazy<WordList?>> Lists = new();
    public static WordList? Dictionary(string code) => Lists.GetOrAdd(code, c => new Lazy<WordList?>(() => Load(c))).Value;
    private static WordList? Load(string code)
    {
        var dic = Path.Combine(DictionaryFolder, code + ".dic");
        var aff = Path.Combine(DictionaryFolder, code + ".aff");
        try { return File.Exists(dic) && File.Exists(aff) ? WordList.CreateFromFiles(dic, aff) : null; }
        catch (Exception) { return null; }
    }

    // ---- Custom words (appdata, one per line) ----
    private static readonly object Gate = new();
    private static HashSet<string>? _custom;
    private static string? _customPath;
    private static HashSet<string> Custom
    {
        get
        {
            lock (Gate)
            {
                if (_custom is not null && _customPath == CustomWordsPath) return _custom;
                _customPath = CustomWordsPath;
                _custom = new HashSet<string>(StringComparer.Ordinal);
                try { if (File.Exists(_customPath)) foreach (var line in File.ReadAllLines(_customPath)) if (line.Trim() is { Length: > 0 } w) _custom.Add(w); }
                catch (Exception) { }
                return _custom;
            }
        }
    }
    public static IReadOnlyList<string> CustomWords { get { var c = Custom; lock (Gate) return [.. c.Order(StringComparer.Ordinal)]; } }

    public static void AddWord(string word)
    {
        var c = Custom;
        lock (Gate)
        {
            if (!c.Add(word)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CustomWordsPath)!);
                File.WriteAllLines(CustomWordsPath, c.Order(StringComparer.Ordinal));
            }
            catch (Exception) { }
        }
        Changed?.Invoke();
    }

    // Tests: forget the cached custom words (re-read from CustomWordsPath).
    public static void Reload() { lock (Gate) _custom = null; }

    // ---- Ignore in this file (per editor, this session) ----
    private static readonly ConditionalWeakTable<object, HashSet<string>> Ignores = new();
    public static void Ignore(object owner, string word)
    {
        var set = Ignores.GetOrCreateValue(owner);
        lock (set) if (!set.Add(word)) return;
        Changed?.Invoke();
    }
    public static IReadOnlySet<string> Ignored(object owner)
    {
        if (!Ignores.TryGetValue(owner, out var set)) return new HashSet<string>();
        lock (set) return new HashSet<string>(set);
    }

    public static bool IsCorrect(string word, string language, IReadOnlySet<string>? ignored = null)
    {
        if (ignored?.Contains(word) == true) return true;
        var custom = Custom;
        lock (Gate) if (custom.Contains(word) || custom.Contains(word.ToLowerInvariant()) && char.IsUpper(word[0])) return true;
        if (Dictionary(language) is not { } list) return true;
        return list.Check(word);
    }

    // Misspelled words in [from, to) of a Markdown document. Safe to call off the UI thread.
    public static List<SpellWord> Misspellings(string text, string language, IReadOnlySet<string>? ignored = null, int from = 0, int to = int.MaxValue) =>
        SpellTokenizer.Words(text, from, to).Where(w => !IsCorrect(w.Word, language, ignored)).ToList();

    // The top 5 suggestions.
    public static IReadOnlyList<string> Suggest(string word, string? language = null)
    {
        if (Dictionary(language ?? ActiveLanguage) is not { } list) return [];
        try { return [.. list.Suggest(word).Where(s => s != word).Take(5)]; }
        catch (Exception) { return []; }
    }
}
