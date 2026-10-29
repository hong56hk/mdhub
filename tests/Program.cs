using System.Text;
using Mdhub.Services;
using Mdhub.Text;

var root = Path.Combine(Path.GetTempPath(), $"mdhub-tests-{Guid.NewGuid():N}");
Require(TextStatistics.Count("") == new TextStatistics(0, 0, 1), "Empty document has one editor line");
Require(TextStatistics.Count("Hello world\r\nNext\n") == new TextStatistics(3, 17, 3), "Statistics count words and mixed line endings");
Require(TextStatistics.Count("🙂 e\u0301").Characters == 3, "Character counts handle emoji and combining accents");
var bold = MarkdownFormatting.Apply("hello world", 5, 0, "bold");
Require(bold.Replacement == "**hello**" && bold.Start == 0 && bold.End == 5, "Formatting supports reversed selections");
var list = MarkdownFormatting.Apply("one\ntwo\nthree", 0, 8, "list");
Require(list.Replacement == "- one\n- two" && list.End == 7, "List formatting excludes unselected next line");
var heading = MarkdownFormatting.Apply("hello world", 7, 7, "h2");
Require(heading.Replacement == "## hello world", "Heading applies to the current line");
Directory.CreateDirectory(root);
try
{
    foreach (var encoding in new Encoding[] { TextFileStore.DefaultEncoding, new UTF8Encoding(true), Encoding.Unicode, Encoding.BigEndianUnicode })
    {
        var path = Path.Combine(root, "roundtrip.txt");
        const string original = "Hello world!\r\nNotes: café 中文 🙂\r\n";
        await File.WriteAllTextAsync(path, original, encoding);
        var loaded = await TextFileStore.ReadAsync(path);
        Require(loaded.Text == original, "Open preserves Unicode and line endings");
        var edited = loaded.Text + "Edited\n";
        await TextFileStore.WriteAsync(path, edited, loaded.Encoding);
        var expected = encoding.GetPreamble().Concat(encoding.GetBytes(edited)).ToArray();
        Require((await File.ReadAllBytesAsync(path)).SequenceEqual(expected), "Save preserves encoding and writes the edited content");
        await TextFileStore.WriteAsync(path, "", loaded.Encoding);
        Require((await TextFileStore.ReadAsync(path)).Text == "", "Saving empty text removes old content");
    }

    var newPath = Path.Combine(root, "new.txt");
    await TextFileStore.WriteAsync(newPath, "New note", TextFileStore.DefaultEncoding);
    Require(await File.ReadAllTextAsync(newPath) == "New note", "Save creates a new file");
    using (var locked = new FileStream(newPath, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        var failed = false;
        try { await TextFileStore.WriteAsync(newPath, "Must not replace", TextFileStore.DefaultEncoding); }
        catch (IOException) { failed = true; }
        if (OperatingSystem.IsWindows()) Require(failed, "Locked destination reports an error");
    }
    if (OperatingSystem.IsWindows())
        Require(await File.ReadAllTextAsync(newPath) == "New note", "Failed save preserves the original file");
    Require(Directory.GetFiles(root, ".mdhub-*.tmp").Length == 0, "Temporary files are cleaned up");

    var invalidPath = Path.Combine(root, "legacy.txt");
    await File.WriteAllBytesAsync(invalidPath, [0xff, 0x61]);
    var rejected = false;
    try { await TextFileStore.ReadAsync(invalidPath); }
    catch (DecoderFallbackException) { rejected = true; }
    Require(rejected, "Unsupported encoding fails instead of silently corrupting text");
    Console.WriteLine("All file tests passed.");
}
finally
{
    Directory.Delete(root, recursive: true);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine($"PASS: {message}");
}
