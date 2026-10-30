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
{
    // Item 25: Open Recent list logic and persistence.
    var sep = Path.DirectorySeparatorChar;
    var baseDir = Path.Combine(Path.GetTempPath(), "mdhub-recent");
    string P(string name) => Path.Combine(baseDir, name);
    var recent = new List<string>();
    foreach (var n in new[] { "a.md", "b.md", "c.md" }) recent = RecentList.Add(recent, P(n));
    Require(recent.SequenceEqual([P("c.md"), P("b.md"), P("a.md")]), "Recent list puts the newest first");
    recent = RecentList.Add(recent, P("a.md"));
    Require(recent.SequenceEqual([P("a.md"), P("c.md"), P("b.md")]), "Reopening moves an entry to the front without duplicating it");
    if (OperatingSystem.IsWindows())
    {
        recent = RecentList.Add(recent, P("B.MD"));
        Require(recent.Count == 3 && recent[0] == P("B.MD"), "Recent list dedupes paths case-insensitively on Windows");
    }
    Require(RecentList.Add(recent, P("x") + sep)[0] == P("x") && RecentList.Same(P("x") + sep, P("x")), "Trailing separators do not make a second entry");
    var many = new List<string>();
    for (var i = 0; i < 15; i++) many = RecentList.Add(many, P($"f{i}.md"));
    Require(many.Count == RecentList.Max && RecentList.Max == 10 && many[0] == P("f14.md") && many[^1] == P("f5.md"), "Recent list keeps the 10 newest");
    Require(RecentList.Remove(many, P("f10.md")).Count == 9 && !RecentList.Remove(many, P("f10.md")).Contains(P("f10.md")), "Removing an entry drops only that entry");
    Require(RecentList.Sanitize([P("a"), null, " ", P("a"), P("b")]).SequenceEqual([P("a"), P("b")]) && RecentList.Sanitize(null).Count == 0
        && RecentList.Sanitize(Enumerable.Range(0, 20).Select(i => P($"s{i}"))).Count == 10, "Loaded lists drop blanks, duplicates and extras");
    var home = Path.Combine(Path.GetTempPath(), "home");
    Require(RecentList.ShortParent(Path.Combine(home, "notes", "x.md"), home) == "~" + sep + "notes" && RecentList.ShortParent(Path.Combine(home, "x.md"), home) == "~",
        "Short path replaces the home folder with ~");
    var deep = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "aaaaaaaaaa", "bbbbbbbbbb", "cccccccccc", "dddddddddd", "eeeeeeeeee", "x.md");
    var shortDeep = RecentList.ShortParent(deep, home);
    Require(shortDeep.Contains("…") && shortDeep.EndsWith("dddddddddd" + sep + "eeeeeeeeee") && shortDeep.Length < 40, $"Long paths shorten to root…last two folders ({shortDeep})");
    Require(RecentList.ShortParent(Path.Combine(baseDir, "y.md"), home) == baseDir.TrimEnd(sep) || baseDir.Length > 40, "Short paths are shown as they are");
    var data = new SessionData { RecentFiles = [P("a.md"), P("b.md")], RecentFolders = [baseDir] };
    var back = SessionStore.Deserialize(SessionStore.Serialize(data))!;
    Require(back.RecentFiles.SequenceEqual(data.RecentFiles) && back.RecentFolders.SequenceEqual(data.RecentFolders), "Recent files and folders round-trip through the session file");
    var old = SessionStore.Deserialize("{\"Version\":1,\"Windows\":[]}")!;
    Require(old.RecentFiles.Count == 0 && old.RecentFolders.Count == 0, "Old session files load with empty recent lists");
    var nulls = SessionStore.Deserialize("{\"Version\":1,\"RecentFiles\":null}");
    Require(nulls is not null && RecentList.Sanitize(nulls.RecentFiles).Count == 0, "A null recent list loads as empty");
}
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
{
{
    // Status-bar caret position: 1-based, CRLF/LF/CR each one line break, selection counts a CRLF as 1.
    var cp = global::Mdhub.Text.CaretPosition.Measure;
    Require(cp("", 0, 0) == (1, 1, 0), "Caret position of empty text is Ln 1, Col 1");
    Require(cp("ab\r\ncd", 6, 6) == (2, 3, 0) && cp("ab\ncd", 3, 3) == (2, 1, 0) && cp("ab\rcd", 4, 4) == (2, 2, 0), "Caret line counts CRLF, LF and CR breaks");
    Require(cp("ab\r\ncd", 3, 3) == (1, 4, 0), "Caret between CR and LF stays on the first line");
    Require(cp("ab\r\ncd\n\nx", 0, 9) == (4, 2, 8), "Selection counts each line break as one character");
    Require(cp("hello", 4, 1) == (1, 2, 3), "Backward selection reports the caret end and its length");
    Require(cp("abc", -5, 99) == (1, 4, 3), "Out-of-range selection is clamped");
    Require(global::Mdhub.Text.CaretPosition.Format("a\n" + new string('x', 1200), 2, 1202) == "Ln 2, Col 1201 (1200 selected)", "Caret position text formats line, column and selection");
    Require(global::Mdhub.Text.CaretPosition.Format("abc", 1, 1) == "Ln 1, Col 2", "No selection count without a selection");
}
    // Lenient search decoding: never throws, detects BOMs, flags undecodable bytes.
    var invalid = Encoding.ASCII.GetBytes("Don't worry: this line has a  smart quote").ToList();
    invalid[29] = 0x92; // cp1252 right quote, invalid in UTF-8 (as in the reported 'Unable to translate bytes [92]')
    var lenient = TextFileStore.DecodeLenient(invalid.ToArray());
    Require(lenient.Lossy && lenient.Text.Contains('\uFFFD') && lenient.Text.StartsWith("Don't worry") && lenient.Text.EndsWith("smart quote"), "Lenient decode replaces invalid UTF-8 bytes instead of throwing");
    Require(!TextFileStore.DecodeLenient(Encoding.UTF8.GetBytes("héllo")).Lossy && TextFileStore.DecodeLenient(Encoding.UTF8.GetBytes("héllo")).Text == "héllo" && TextFileStore.DecodeLenient(Encoding.UTF8.GetBytes("héllo")).Encoding.GetPreamble().Length == 0, "Lenient decode keeps valid BOM-less UTF-8");
    foreach (var bomEncoding in new Encoding[] { new UTF8Encoding(true), Encoding.Unicode, Encoding.BigEndianUnicode, Encoding.UTF32, new UTF32Encoding(true, true) })
    {
        var decoded = TextFileStore.DecodeLenient([.. bomEncoding.GetPreamble(), .. bomEncoding.GetBytes("ab\r\nçd")]);
        Require(decoded.Text == "ab\r\nçd" && !decoded.Lossy && decoded.Encoding.GetPreamble().SequenceEqual(bomEncoding.GetPreamble()), $"Lenient decode detects {bomEncoding.WebName} BOM");
    }
    var badUtf8Bom = TextFileStore.DecodeLenient([0xEF, 0xBB, 0xBF, (byte)'a', 0xFF, (byte)'b']);
    Require(badUtf8Bom.Lossy && badUtf8Bom.Text == "a\uFFFDb", "Lenient decode replaces invalid bytes after a UTF-8 BOM");
    Require(TextFileStore.DecodeLenient([]).Text == "" && !TextFileStore.DecodeLenient([]).Lossy, "Lenient decode handles empty files");
    var lenientPath = Path.Combine(Path.GetTempPath(), "mdhub-lenient-" + Guid.NewGuid().ToString("N") + ".md");
    await File.WriteAllBytesAsync(lenientPath, invalid.ToArray());
    var read = await TextFileStore.ReadLenientAsync(lenientPath);
    Require(read.Lossy && read.Text == lenient.Text, "ReadLenientAsync reads invalid bytes without throwing");
    File.Delete(lenientPath);
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
    var stamp = new DateTime(2026, 10, 29, 13, 5, 9);
    var notePath = Path.Combine(root, "My note.md");
    Require(ImageAssets.FileName(notePath, ".png", stamp, _ => false) == "My note-20261029130509.png", "Pasted bitmap is named <note>-<yyyyMMddHHmmss>.png");
    Require(ImageAssets.FileName(notePath, ".JPG", stamp, _ => false) == "My note-20261029130509.JPG" && ImageAssets.FileName(notePath, "gif", stamp, _ => false) == "My note-20261029130509.gif",
        "Pasted file keeps its original extension");
    var taken = new HashSet<string> { "My note-20261029130509.png", "My note-20261029130509-1.png" };
    Require(ImageAssets.FileName(notePath, ".png", stamp, taken.Contains) == "My note-20261029130509-2.png", "Name collisions add -1, -2, ...");
    Require(ImageAssets.MarkdownLink("My note-20261029130509.png") == "![My note-20261029130509](.assets/My%20note-20261029130509.png)"
        && ImageAssets.MarkdownLink("a(1).png") == "![a(1)](.assets/a%281%29.png)", "Image link is relative with forward slashes and encoded spaces");
    Require(ImageAssets.IsImageFile("x.PNG") && !ImageAssets.IsImageFile("x.txt"), "Only image files are pasted as images");
    {
        string Spans(FindResult r) => string.Join(",", r.Matches.Select(m => $"{m.Start}:{m.Length}"));
        var sample = "Cat cat CAT concat cat_x";
        Require(Spans(FindReplace.FindAll(sample, "cat", new())) == "0:3,4:3,8:3,15:3,19:3", "Find is case-insensitive by default");
        Require(Spans(FindReplace.FindAll(sample, "cat", new(MatchCase: true))) == "4:3,15:3,19:3", "Match case finds only exact case");
        Require(Spans(FindReplace.FindAll(sample, "cat", new(WholeWord: true))) == "0:3,4:3,8:3", "Whole word skips matches inside words and before underscores");
        Require(Spans(FindReplace.FindAll("a.b axb", "a.b", new())) == "0:3", "Without regex, special characters are literal");
        Require(Spans(FindReplace.FindAll("a.b axb", "a.b", new(UseRegex: true))) == "0:3,4:3", "Regex mode interprets patterns");
        Require(Spans(FindReplace.FindAll("one\ntwo\nthree", "^t", new(UseRegex: true))) == "4:1,8:1", "Regex anchors are per line");
        Require(FindReplace.FindAll("abc", "a*?", new(UseRegex: true)).Matches.Count == 0, "Empty regex matches are ignored");
        var bad = FindReplace.FindAll("abc", "(a", new(UseRegex: true));
        Require(!bad.IsValid && bad.Matches.Count == 0 && bad.Error is not null, "Invalid regex reports an error and no matches");
        Require(FindReplace.FindAll("abc", "", new()) is { IsValid: true, Matches.Count: 0 }, "Empty query has no results and no error");
        Require(Spans(FindReplace.FindAll("cat cat cat cat", "cat", new(), (4, 11))) == "4:3,8:3", "Search is limited to the selection range");
        Require(Spans(FindReplace.FindAll("cat cat", "cat", new(), (6, 0))) == "0:3", "Reversed range is normalized; partial matches at the edge are excluded");
        Require(Spans(FindReplace.FindAll("xcat cat", "cat", new(WholeWord: true), (1, 8))) == "5:3", "Whole word in a range still uses the text outside the range");
        Require(FindReplace.PreserveCase("CAT", "dog") == "DOG" && FindReplace.PreserveCase("cat", "Dog") == "dog"
            && FindReplace.PreserveCase("Cat", "dOG") == "Dog" && FindReplace.PreserveCase("123", "Dog") == "Dog", "Preserve case mirrors upper, lower and capitalized");
        Require(FindReplace.Replacement("John Smith", new FindMatch(0, 10), @"(\w+) (\w+)", "$2, $1", new(UseRegex: true)) == "Smith, John", "Regex replacement substitutes numbered groups");
        Require(FindReplace.Replacement("k=v", new FindMatch(0, 3), @"(?<key>\w)=(?<val>\w)", "${val}=${key}", new(UseRegex: true)) == "v=k", "Regex replacement substitutes named groups");
        Require(FindReplace.Replacement("a.b", new FindMatch(0, 3), "a.b", "$1", new()) == "$1", "Literal mode does not expand $ in replacements");
        Require(FindReplace.Replacement("Cat", new FindMatch(0, 3), "cat", "dog", new(PreserveCase: true)) == "Dog", "Preserve case applies to a replacement");
        var all = FindReplace.ReplaceAll("Cat cat CAT", "cat", "dog", new(PreserveCase: true));
        Require(all.Text == "Dog dog DOG" && all.Count == 3 && all.Start == 0 && all.End == 11 && all.Segment == "Dog dog DOG", "Replace all with preserve case");
        var ranged = FindReplace.ReplaceAll("a1 a2 a3 a4", @"a(\d)", "b$1", new(UseRegex: true), (3, 8));
        Require(ranged.Text == "a1 b2 b3 a4" && ranged.Count == 2 && ranged.Start == 3 && ranged.End == 8, "Replace all respects the range and regex groups");
        Require(FindReplace.ReplaceAll("abc", "z", "y", new()).Count == 0, "Replace all with no matches changes nothing");
        var found = FindReplace.FindAll("x x x", "x", new()).Matches;
        Require(FindReplace.NextIndex(found, 1) == 1 && FindReplace.NextIndex(found, 5) == 0 && FindReplace.PreviousIndex(found, 3) == 1 && FindReplace.PreviousIndex(found, 0) == 2,
            "Next and previous wrap around");
        Require(FindReplace.CountLabel(1, 10) == "2 of 10" && FindReplace.CountLabel(-1, 0) == "No results", "Count reads '2 of 10' or 'No results'");
    }
    {
        var sessionDir = Path.Combine(root, "session-" + Guid.NewGuid().ToString("N"));
        var sessionFile = Path.Combine(sessionDir, "mdhub", "session.json");
        var keptFolder = Directory.CreateDirectory(Path.Combine(sessionDir, "notes")).FullName;
        var keptA = Path.Combine(keptFolder, "a.md"); File.WriteAllText(keptA, "a");
        var keptB = Path.Combine(keptFolder, "b.md"); File.WriteAllText(keptB, "b");
        var gone = Path.Combine(keptFolder, "gone.md");
        var session = new Mdhub.Services.SessionData
        {
            Windows =
            [
                new() { Folder = keptFolder, Tabs = [keptA, keptB], ActiveTab = 1, X = 10, Y = 20, Width = 900, Height = 600, State = "Maximized" },
                new() { Folder = Path.Combine(sessionDir, "missing"), Tabs = [gone], ActiveTab = 0 },
                new() { Folder = null, Tabs = [gone, keptA], ActiveTab = 0 }
            ]
        };
        Mdhub.Services.SessionStore.Save(sessionFile, session);
        var loaded = Mdhub.Services.SessionStore.Load(sessionFile)!;
        Require(loaded.Windows.Count == 3 && loaded.Windows[0].Folder == keptFolder && loaded.Windows[0].Tabs.SequenceEqual([keptA, keptB])
            && loaded.Windows[0].ActiveTab == 1 && loaded.Windows[0].X == 10 && loaded.Windows[0].Height == 600 && loaded.Windows[0].State == "Maximized",
            "Session round-trips folders, tabs, active tab and window bounds");
        Require(Directory.GetFiles(Path.GetDirectoryName(sessionFile)!).Length == 1, "Session save leaves no temporary file");
        var (valid, skipped) = Mdhub.Services.SessionStore.Validate(loaded, Directory.Exists, File.Exists);
        Require(valid.Count == 2 && skipped == 3 && valid[0].Tabs.Count == 2 && valid[0].ActiveTab == 1
            && valid[1].Folder is null && valid[1].Tabs.SequenceEqual([keptA]) && valid[1].ActiveTab == 0,
            "Validation drops missing folders and files, counts them, and drops windows with nothing left");
        var activeGone = new Mdhub.Services.SessionData { Windows = [new() { Folder = keptFolder, Tabs = [keptA, gone], ActiveTab = 1 }] };
        Require(Mdhub.Services.SessionStore.Validate(activeGone, Directory.Exists, File.Exists).Windows[0].ActiveTab == 0, "A missing active tab falls back to the first kept tab");
        var relative = new Mdhub.Services.SessionData { Windows = [new() { Folder = "notes", Tabs = ["a.md", ""] }] };
        Require(Mdhub.Services.SessionStore.Validate(relative, _ => true, _ => true).Windows.Count == 0, "Relative paths are never restored");
        var invalid = new Mdhub.Services.SessionData { Windows = [new() { Folder = "C:\\bad|<dir>", Tabs = ["C:\\bad|name.md"] }] };
        Require(Mdhub.Services.SessionStore.Validate(invalid, Directory.Exists, File.Exists) is { Windows.Count: 0, Skipped: 2 }, "Invalid paths are skipped without throwing");
        Require(Mdhub.Services.SessionStore.Validate(null, Directory.Exists, File.Exists) is { Windows.Count: 0, Skipped: 0 }, "No session validates to nothing");
        foreach (var bad in new[] { "", "{", "not json", "[]", "null", "{\"Version\":2,\"Windows\":[]}", "{\"Windows\":[]}", "{\"Version\":\"1\"}", "{\"Version\":1,\"Windows\":{}}", "{\"Version\":1,\"Windows\":null}" })
            Require(Mdhub.Services.SessionStore.Deserialize(bad) is null, $"Corrupt or wrong-schema session is ignored: {bad}");
        Require(Mdhub.Services.SessionStore.Deserialize("{\"Version\":1,\"Windows\":[{\"Folder\":null,\"Tabs\":null}]}") is { Windows.Count: 1 } nulls && nulls.Windows[0].Tabs.Count == 0,
            "Null tab lists load as empty");
        Require(Mdhub.Services.SessionStore.Deserialize("{\"Version\":1,\"Windows\":[]}")?.Theme == "Dark", "Old session without a theme loads as Dark");
        Require(Mdhub.Services.SessionStore.Deserialize(Mdhub.Services.SessionStore.Serialize(new Mdhub.Services.SessionData { Theme = "Light" }))?.Theme == "Light", "Theme choice round-trips through the session file");
        Require(Mdhub.Services.SessionStore.Load(Path.Combine(sessionDir, "nope.json")) is null, "Missing session file loads as null");
        File.WriteAllText(sessionFile, "{ broken");
        Require(Mdhub.Services.SessionStore.Load(sessionFile) is null, "Corrupt session file loads as null");
        Mdhub.Services.SessionStore.Save(sessionFile, session);
        var before = File.ReadAllText(sessionFile);
        using (new FileStream(sessionFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failed = false;
            try { Mdhub.Services.SessionStore.Save(sessionFile, new Mdhub.Services.SessionData()); } catch (IOException) { failed = true; } catch (UnauthorizedAccessException) { failed = true; }
            Require(failed, "Replacing a locked session file fails");
        }
        Require(File.ReadAllText(sessionFile) == before && Directory.GetFiles(Path.GetDirectoryName(sessionFile)!).Length == 1,
            "A failed session write keeps the previous file intact and removes the temporary file");
        Mdhub.Services.SessionStore.Save(sessionFile, new Mdhub.Services.SessionData());
        Require(Mdhub.Services.SessionStore.Load(sessionFile) is { Windows.Count: 0 }, "Session save replaces an existing file");
        Directory.Delete(sessionDir, true);
    }
    {
        var screens = new List<ScreenArea> { new(0, 0, 1920, 1040), new(1920, 0, 1280, 984) };
        var primary = screens[0];
        Require(WindowPlacement.Place(100, 80, 900, 640, screens, primary) == (100, 80), "An on-screen position is kept");
        Require(WindowPlacement.Place(2200, 100, 900, 640, screens, primary) == (2200, 100), "A position on a second monitor is kept");
        Require(WindowPlacement.Place(1700, 50, 900, 640, screens, primary) == (1700, 50), "A window partly on screen with a reachable title bar is kept");
        Require(WindowPlacement.Place(3400, 100, 900, 640, [primary], primary) == (510, 200), "A position on a disconnected monitor moves to the primary screen centre");
        Require(WindowPlacement.Place(-5000, -5000, 900, 640, screens, primary) == (510, 200), "A far off-screen position moves to the primary screen");
        Require(WindowPlacement.Place(100, -300, 900, 640, screens, primary) == (510, 200), "A window whose title bar is above every screen is moved");
        Require(WindowPlacement.Place(100, 1030, 900, 640, screens, primary) == (510, 200), "A window with only a sliver showing is moved");
        Require(WindowPlacement.Place(10, 10, 4000, 3000, [primary], new ScreenArea(0, 0, 1920, 1040)) == (10, 10)
            && WindowPlacement.Place(-9000, 0, 4000, 3000, [primary], primary) == (0, 0), "An oversized window is placed at the primary screen origin");
        Require(WindowPlacement.Place(-9000, 0, 900, 640, [new ScreenArea(-1280, 0, 1280, 1024), primary], new ScreenArea(-1280, 0, 1280, 1024)) == (-1090, 192),
            "The fallback uses the primary screen even when it has negative coordinates");
        Require(WindowPlacement.Place(-9000, 0, 900, 640, [], primary) == (-9000, 0), "With no screen information the position is left alone");
    }
    {
        // "# Hi\n\nab **cd** ef\n\n| t |\n|---|\n| u |\n"
        var src = "# Hi\n\nab **cd** ef\n\n| t |\n|---|\n| u |\n";
        var map = new Mdhub.Rendering.PreviewSourceMap(src);
        var h = map.AddBlock(0, 4); map.AddSegment(h, 2, "Hi");
        var p = map.AddBlock(6, 18);
        var s1 = map.AddSegment(p, 6, "ab "); var s2 = map.AddSegment(p, 11, "cd"); var s3 = map.AddSegment(p, 15, " ef");
        var tbl = map.AddBlock(20, src.Length, isContainer: true);
        var c1 = map.AddBlock(22, 23, tbl); map.AddSegment(c1, 22, "t");
        var c2 = map.AddBlock(34, 35, tbl); map.AddSegment(c2, 34, "u");
        Require(map.AddSegment(p, 6, "a&b") == -1, "Rendered text that differs from the source (escapes, entities) is not mapped");
        var exact = map.Map(7, 8);
        Require(exact.Count == 1 && exact[0] == new Mdhub.Rendering.PreviewSourceMap.Highlight(p, s1, 1, 1, false), "A selection inside one run maps to exactly that range");
        Require(map.Map(8, 7).SequenceEqual(exact), "Reversed selections map the same way");
        var bolded = map.Map(11, 13);
        Require(bolded.Count == 1 && bolded[0].SegmentId == s2 && bolded[0].Length == 2 && !bolded[0].WholeBlock, "Text inside emphasis maps exactly");
        var markup = map.Map(8, 13);
        Require(markup.Count == 1 && markup[0].WholeBlock && markup[0].BlockId == p, "A selection that includes markup characters falls back to the whole block");
        var headSel = map.Map(0, 4);
        Require(headSel.Count == 1 && headSel[0].WholeBlock && headSel[0].BlockId == h, "Selecting a heading marker highlights the whole heading");
        var two = map.Map(2, 7);
        Require(two.Count == 2 && !two[0].WholeBlock && two[0].Length == 2 && !two[1].WholeBlock && two[1].SegmentId == s1 && two[1].Length == 1,
            "A selection across blocks maps each block; the blank line between them is ignored");
        Require(map.Map(5, 5).Count == 0 && map.Map(4, 6).Count == 0, "An empty or whitespace-only selection maps to nothing");
        var cell = map.Map(34, 35);
        Require(cell.Count == 1 && cell[0].BlockId == c2 && !cell[0].WholeBlock, "Text inside one table cell maps exactly");
        var partial = map.Map(22, 27);
        Require(partial.Count == 1 && partial[0].WholeBlock && partial[0].BlockId == tbl, "A partial table selection with pipes highlights the whole table");
        Require(map.Map(-5, 999).Any(x => x.WholeBlock && x.BlockId == tbl) && map.Map(-5, 999).Count(x => x.BlockId == tbl) == 1, "Out-of-range selections are clamped");
        _ = s3;
    }    {
        Require(ZoomLevel.In(100) == 110 && ZoomLevel.Out(100) == 90, "Zoom steps are 10%");
        Require(ZoomLevel.In(200) == 200 && ZoomLevel.Out(50) == 50 && ZoomLevel.Clamp(10) == 50 && ZoomLevel.Clamp(500) == 200, "Zoom is clamped to 50%-200%");
        var z = 100; for (var i = 0; i < 37; i++) z = ZoomLevel.In(z); for (var i = 0; i < 9; i++) z = ZoomLevel.Out(z);
        Require(z == 110, "Repeated steps stay on whole percents (no drift)");
        Require(ZoomLevel.Normalize(123) == 120 && ZoomLevel.Normalize(125) == 130 && ZoomLevel.Normalize(-40) == 50 && ZoomLevel.In(123) == 130, "Off-grid values snap to the 10% grid");
        Require(ZoomLevel.Wheel(100, 1) == 110 && ZoomLevel.Wheel(100, -0.5) == 90 && ZoomLevel.Wheel(100, 0) == 100, "Wheel up zooms in, down zooms out");
        Require(ZoomLevel.Scale(120) == 1.2 && ZoomLevel.Label(120) == "Zoom 120%", "Zoom scale and status label");
        var zs = new SessionData { Windows = [new WindowSession { Folder = "C:/a", Zoom = 140 }, new WindowSession { Folder = "C:/b" }] };
        var back = SessionStore.Deserialize(SessionStore.Serialize(zs))!;
        Require(back.Windows[0].Zoom == 140 && back.Windows[1].Zoom == 100, "Window zoom round-trips through the session file");
        var old = SessionStore.Deserialize("{\"Version\":1,\"Windows\":[{\"Folder\":\"C:/a\",\"Tabs\":[],\"Width\":900,\"Height\":600}]}");
        Require(old is { Windows.Count: 1 } && old.Windows[0].Zoom == 100, "Old session files without zoom default to 100%");
    }    {
        var cl = "ab\r\ncd\n\nef\rgh";
        Require(CaretLine.At(cl, 0) == (0, 2) && CaretLine.At(cl, 2) == (0, 2) && CaretLine.At(cl, 4) == (4, 6) && CaretLine.At(cl, 6) == (4, 6),
            "The caret line excludes CRLF/LF breaks, with the caret at either end");
        Require(CaretLine.At(cl, 7) == (7, 7) && CaretLine.At(cl, 10) == (8, 10) && CaretLine.At(cl, 12) == (11, 13) && CaretLine.At(cl, 99) == (11, 13) && CaretLine.At("", 0) == (0, 0),
            "Blank lines, bare CR breaks, the last line, out-of-range carets and empty text");
        var bsrc = "# Hi\n\nab\ncd\n\n| t |\n|---|\n| u |\n";
        var bmap = new Mdhub.Rendering.PreviewSourceMap(bsrc);
        var bh = bmap.AddBlock(0, 4); var bp = bmap.AddBlock(6, 11);
        var bt = bmap.AddBlock(13, bsrc.Length - 1, isContainer: true); bmap.AddBlock(15, 16, bt);
        Require(bmap.BlockForLine(0, 4) == bh && bmap.BlockForLine(9, 11) == bp && bmap.BlockForLine(6, 8) == bp, "Each source line maps to its rendered block (multi-line paragraphs too)");
        Require(bmap.BlockForLine(5, 5) == -1 && bmap.BlockForLine(12, 12) == -1, "Blank lines between blocks map to nothing");
        Require(bmap.BlockForLine(13, 18) == bt && bmap.BlockForLine(25, 30) == bt, "Lines inside a table map to the whole table");
    }    {
        Require(!Shortcuts.Conflicts().Any(), "No two shortcuts share a gesture");
        Require(Shortcuts.All.Select(s => s.Id).Distinct().Count() == Shortcuts.All.Count && Shortcuts.All.All(s => s.Gestures.Count > 0 && s.Label.Length > 0), "Shortcut ids are unique and every entry has a label and gesture");
        Require(Enum.GetValues<ShortcutArea>().All(a => Shortcuts.All.Any(s => s.Area == a)), "Every area has at least one shortcut");
        Require(Shortcuts.Format("file.saveAs", false) == "Ctrl+Shift+S" && Shortcuts.Format("file.saveAs", true) == "Cmd+Shift+S", "Command shortcuts format as Ctrl on Windows and Cmd on macOS");
        Require(Shortcuts.Format("zoom.in", false) == "Ctrl+= / Ctrl+Shift+= / Ctrl+Num +" && Shortcuts.Format("zoom.reset", true) == "Cmd+0 / Cmd+Num 0", "Multiple gestures and key names are formatted");
        Require(Shortcuts.Format("find.previous", true) == "Shift+F3" && Shortcuts.Format("view.wordWrap", false) == "Alt+Z" && Shortcuts.Format("view.wordWrap", true) == "Option+Z", "Shift and Alt (Option on macOS) format");
        Require(Shortcuts.Get("edit.undo").HandledByTextBox && !Shortcuts.Get("file.save").HandledByTextBox, "Entries record whether the editor handles them itself");
    }    {
        Require(new[] { ("editor.bold", "Ctrl+B", "Cmd+B"), ("editor.italic", "Ctrl+I", "Cmd+I"), ("editor.link", "Ctrl+L", "Cmd+L"), ("editor.quote", "Ctrl+\"", "Cmd+\""),
            ("editor.insertTime", "Ctrl+;", "Cmd+;"), ("editor.goToLine", "Ctrl+G", "Cmd+G"), ("view.previewCaret", "Ctrl+Shift+G", "Cmd+Shift+G"), ("find.replace", "Ctrl+H", "Cmd+H") }
            .All(x => Shortcuts.Format(x.Item1, false) == x.Item2 && Shortcuts.Format(x.Item1, true) == x.Item3), "Item 18 shortcuts are in the table with Windows and macOS text");
        var quote = Shortcuts.Get("editor.quote");
        Require(quote.Gestures.Count == 2 && quote.Gestures.All(g => g.Key == "OemQuotes") && quote.Gestures.Count(g => g.Hidden) == 1, "Block quote accepts Ctrl+' and Ctrl+Shift+' but shows one gesture");
        Require(!Shortcuts.Conflicts().Any() && new[] { "editor.bold", "editor.italic", "editor.link", "editor.quote", "editor.insertTime", "editor.goToLine", "view.previewCaret" }.All(id => !Shortcuts.Get(id).HandledByTextBox),
            "New shortcuts do not clash and are handled by mdhub");
        var dup = Shortcuts.All.SelectMany(s => s.Gestures.Select(g => (g.Key, g.Mods))).GroupBy(k => k).Where(g => g.Count() > 1).ToList();
        Require(dup.Count == 0, "No key combination appears twice (display-only differences ignored)");
        var savedCulture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fi-FI");
        Require(EditorCommands.Timestamp(new DateTime(2026, 3, 4, 5, 6, 7)) == "2026-03-04 05:06:07" && EditorCommands.Timestamp(new DateTime(2026, 12, 31, 23, 59, 59)) == "2026-12-31 23:59:59",
            "Timestamps use yyyy-MM-dd HH:mm:ss regardless of culture");
        System.Globalization.CultureInfo.CurrentCulture = savedCulture;
        Require(EditorCommands.LineCount("") == 1 && EditorCommands.LineCount("a\nb") == 2 && EditorCommands.LineCount("a\r\nb\rc\n") == 4, "Line count handles \\n, \\r\\n and \\r");
        Require(EditorCommands.ParseLine("0", 10) == 1 && EditorCommands.ParseLine("5", 3) == 3 && EditorCommands.ParseLine("007", 10) == 7 && EditorCommands.ParseLine("99999999999999", 12) == 12,
            "Line numbers clamp to 1..N");
        Require(EditorCommands.ParseLine("", 5) is null && EditorCommands.ParseLine("a1", 5) is null && EditorCommands.ParseLine("-2", 5) is null && EditorCommands.ParseLine(" 3", 5) is null, "Non-digit line input is rejected");
        Require(EditorCommands.LineStart("a\r\nbb\nc", 1) == 0 && EditorCommands.LineStart("a\r\nbb\nc", 2) == 3 && EditorCommands.LineStart("a\r\nbb\nc", 3) == 6 && EditorCommands.LineStart("a\r\nbb\nc", 9) == 7
            && EditorCommands.LineStart("x\n", 2) == 2, "Line start offsets are correct");
    }    {
        Func<string, bool> any = _ => true;
        static global::Mdhub.Rendering.EditPlan Dummy() => default;
        Require(global::Mdhub.Rendering.PreviewEdit.Diff("hello", "help me") == (3, 2, "p me") && global::Mdhub.Rendering.PreviewEdit.Diff("abc", "abc") == (3, 0, "") && global::Mdhub.Rendering.PreviewEdit.Diff("aa", "aaa") == (2, 0, "a"), "Preview edit diff finds the changed range");
        Require(global::Mdhub.Rendering.PreviewEdit.EscapeInline("a*b_c [x](y) <t> `q` #1 | & ! ~ \\", false) == "a\\*b\\_c \\[x\\](y) \\<t\\> \\`q\\` \\#1 \\| \\& \\! \\~ \\\\", "Inline Markdown punctuation is escaped");
        Require(global::Mdhub.Rendering.PreviewEdit.EscapeInline("- item", true) == "\\- item" && global::Mdhub.Rendering.PreviewEdit.EscapeInline("+x", true) == "\\+x" && global::Mdhub.Rendering.PreviewEdit.EscapeInline("12. x", true) == "12\\. x"
            && global::Mdhub.Rendering.PreviewEdit.EscapeInline("3) x", true) == "3\\) x" && global::Mdhub.Rendering.PreviewEdit.EscapeInline("- item", false) == "- item" && global::Mdhub.Rendering.PreviewEdit.EscapeInline("12. x", false) == "12. x", "List starters are escaped only at the start of a line");
        var src = "a **bold** c";
        global::Mdhub.Rendering.EditPiece[] pieces = [new("a ", 0), new("bold", 4), new(" c", 10)];
        var grow = global::Mdhub.Rendering.PreviewEdit.PlanInline(src, pieces, "a bolder c", any);
        Require(grow.Edit == new global::Mdhub.Rendering.SourceEdit(8, 8, "er"), "Typing inside bold text edits the bold source range");
        var head = global::Mdhub.Rendering.PreviewEdit.PlanInline(src, pieces, "xa bold c", any);
        Require(head.Edit == new global::Mdhub.Rendering.SourceEdit(0, 0, "x"), "Typing at the start edits the first piece");
        Require(global::Mdhub.Rendering.PreviewEdit.PlanInline(src, pieces, "a bold c", any).NoChange, "Unchanged text is no edit");
        Require(global::Mdhub.Rendering.PreviewEdit.PlanInline(src, pieces, "aXold c", any).Error == global::Mdhub.Rendering.PreviewEdit.SpansFormatting, "Edits crossing formatting are rejected");
        Require(global::Mdhub.Rendering.PreviewEdit.PlanInline(src, pieces, "a bo\nld c", any).Error == global::Mdhub.Rendering.PreviewEdit.NoLineBreaks, "Line breaks are rejected");
        Require(global::Mdhub.Rendering.PreviewEdit.PlanInline(src, pieces, "a bold *c*", any).Edit == new global::Mdhub.Rendering.SourceEdit(11, 12, "\\*c\\*"), "Typed Markdown characters are escaped");
        Require(global::Mdhub.Rendering.PreviewEdit.PlanInline(src, pieces, "a bold c", s => false).NoChange && global::Mdhub.Rendering.PreviewEdit.PlanInline(src, pieces, "a bold cc", s => false).Error == global::Mdhub.Rendering.PreviewEdit.ChangesStructure,
            "An edit that changes the structure is rejected");
        var right = global::Mdhub.Rendering.PreviewEdit.PlanInline(src, pieces, "a boldx c", s => !s.Contains("boldx**"));
        Require(right.Edit == new global::Mdhub.Rendering.SourceEdit(10, 10, "x"), "A boundary insertion falls back to the next piece when the first would break formatting");
        var left = global::Mdhub.Rendering.PreviewEdit.PlanInline(src, pieces, "a boldx c", any);
        Require(left.Edit == new global::Mdhub.Rendering.SourceEdit(8, 8, "x"), "A boundary insertion prefers the piece on the left");
        global::Mdhub.Rendering.EditPiece[] unmapped = [new("see ", 0), new("<x>", -1), new(" end", 7)];
        Require(global::Mdhub.Rendering.PreviewEdit.PlanInline("see <x> end", unmapped, "see <y> end", any).Error == global::Mdhub.Rendering.PreviewEdit.SpansFormatting, "Unmapped (markup) text cannot be edited");
        global::Mdhub.Rendering.EditPiece[] code = [new("use ", 0), new("a*b", 5, true)];
        Require(global::Mdhub.Rendering.PreviewEdit.PlanInline("use `a*b`", code, "use a*bc", any).Edit == new global::Mdhub.Rendering.SourceEdit(8, 8, "c") && global::Mdhub.Rendering.PreviewEdit.PlanInline("use `a*b`", code, "use a*_b", any).Edit == new global::Mdhub.Rendering.SourceEdit(7, 7, "_"),
            "Inline code is edited without escaping");
        Require(global::Mdhub.Rendering.PreviewEdit.PlanInline("use `a*b`", code, "use a`b", any).Error == global::Mdhub.Rendering.PreviewEdit.Backtick, "Backticks are rejected in inline code");
        global::Mdhub.Rendering.EditPiece[] lines = [new("one", 0), new("\n", -1), new("two", 4)];
        Require(global::Mdhub.Rendering.PreviewEdit.PlanInline("one\ntwo", lines, "one\n- two", any).Edit == new global::Mdhub.Rendering.SourceEdit(4, 4, "\\- "), "Line start after a soft break is escaped");
        {
            // Rich copy (item 24): CF_HTML byte offsets, plain text without markers, RTF escaping.
            var cf = global::Mdhub.Rendering.RichCopy.CfHtml("<p>caf\u00e9 \u4e2d \U0001F600</p>");
            var bytes = System.Text.Encoding.UTF8.GetBytes(cf);
            int H(string name) => int.Parse(System.Text.RegularExpressions.Regex.Match(cf, name + @":(\d{10})\r\n").Groups[1].Value);
            string B(int a, int b) => System.Text.Encoding.UTF8.GetString(bytes, a, b - a);
            Require(B(H("StartFragment"), H("EndFragment")) == "<p>caf\u00e9 \u4e2d \U0001F600</p>" && B(H("StartHTML"), H("StartHTML") + 6) == "<html>" && H("EndHTML") == bytes.Length
                && B(0, H("StartHTML")).StartsWith("Version:0.9\r\nStartHTML:") && bytes.Length > cf.Length, "CF_HTML header offsets are UTF-8 byte offsets of the HTML and fragment");
            Require(global::Mdhub.Rendering.RichCopy.CfHtml("").Contains("<!--StartFragment--><!--EndFragment-->") && H("StartFragment") < H("EndFragment"), "CF_HTML handles an empty fragment");
            global::Mdhub.Rendering.CopyNode N(string kind, params global::Mdhub.Rendering.CopyNode[] children) { var n = new global::Mdhub.Rendering.CopyNode(kind); n.Children.AddRange(children); return n; }
            global::Mdhub.Rendering.CopyNode T(string text) => new("text") { Text = text };
            var link = new global::Mdhub.Rendering.CopyNode("a") { Href = "https://x.test/?a=1&b=\"2\"" }; link.Children.Add(T("x<y"));
            var doc = N("doc", N("h2", T("Head")), N("p", T("a "), N("strong", T("b")), T(" {c}\\ "), link), N("ol") , N("table", N("tr", N("th", N("p", T("A"))), N("th", N("p", T("B")))), N("tr", N("td", N("p", T("1"))), N("td", N("p", T("2"))))));
            doc.Children[2].Children.Add(N("li", N("p", T("one")))); doc.Children[2].Children.Add(N("li", N("p", T("two"))));
            var style = new global::Mdhub.Rendering.RichCopyStyle("#0000FF", "#EEEEEE", "#DDDDDD", "#CCCCCC", "#F0F0F0", "#999999");
            Require(global::Mdhub.Rendering.RichCopy.Plain(doc) == "Head\n\na b {c}\\ x<y\n\n1. one\n2. two\n\nA\tB\n1\t2", "Rich copy plain text drops markup and keeps list numbers and cell tabs");
            var html = global::Mdhub.Rendering.RichCopy.Html(doc, style);
            Require(html.Contains("<a href=\"https://x.test/?a=1&amp;b=&quot;2&quot;\" style=\"color:#0000FF;text-decoration:underline\">x&lt;y</a>") && html.Contains("<th style=\"border:1px solid #CCCCCC;") && html.Contains("<h2 style=\"font-size:23px"),
                "Rich copy HTML escapes text and attributes and inlines styles");
            var rtf = global::Mdhub.Rendering.RichCopy.Rtf(doc, style);
            Require(rtf.Contains(" \\{c\\}\\\\ ") && rtf.Contains("\\red0\\green0\\blue255;") && rtf.Contains("1.\\tab one") && rtf.Contains("\\cellx4800") && rtf.Contains("{\\b\\fs46 Head}"), "Rich copy RTF escapes braces and backslashes and writes lists and tables");
        }
        var fence = "```\r\nlet x;\r\n```\r\n";
        var codePlan = global::Mdhub.Rendering.PreviewEdit.PlanCode(fence, 5, 11, "let x;\nlet y;", any);
        Require(codePlan.Edit is { } ce && fence[..ce.Start] + ce.Replacement + fence[ce.End..] == "```\r\nlet x;\r\nlet y;\r\n```\r\n", "Code block edits keep the note's CRLF line breaks");
        Require(global::Mdhub.Rendering.PreviewEdit.PlanCode(fence, 5, 11, "let x;", any).NoChange && global::Mdhub.Rendering.PreviewEdit.PlanCode(fence, 5, 11, "```", _ => false).Error == global::Mdhub.Rendering.PreviewEdit.ChangesStructure, "Code edits that close the fence are rejected");
    }    Console.WriteLine("All file tests passed.");
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
