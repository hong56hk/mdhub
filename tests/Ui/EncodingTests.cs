using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Text;
using Mdhub.Views;
using IOPath = System.IO.Path;

// Item 67 (VS Code): CJK and legacy encodings: detection order, round-trip saves, UTF-16 is not binary, search in
// legacy files (with the per-file encoding cache), the unencodable-character fallback and the Default encoding setting.
internal static class EncodingTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    const string Ja = "\u65e5\u672c\u8a9e\u306e\u30c6\u30ad\u30b9\u30c8\u3067\u3059\u3002\u3053\u3093\u306b\u3061\u306f\u4e16\u754c";
    const string Ko = "\ud55c\uad6d\uc5b4 \ud14d\uc2a4\ud2b8\uc785\ub2c8\ub2e4. \uc548\ub155\ud558\uc138\uc694 \uc138\uacc4";
    const string Hans = "\u4e2d\u6587\u7b80\u4f53\u6d4b\u8bd5\u6587\u672c\uff0c\u4f60\u597d\u4e16\u754c\u3002\u6211\u4eec\u559c\u6b22\u7f16\u7a0b";
    const string Hant = "\u7e41\u9ad4\u4e2d\u6587\u6e2c\u8a66\u6587\u5b57\uff0c\u4f60\u597d\u4e16\u754c\u3002\u6211\u5011\u559c\u6b61\u7a0b\u5f0f\u8a2d\u8a08";
    const string Ru = "\u041f\u0440\u0438\u0432\u0435\u0442, \u043c\u0438\u0440! \u042d\u0442\u043e \u0442\u0435\u0441\u0442\u043e\u0432\u044b\u0439 \u0442\u0435\u043a\u0441\u0442 \u043d\u0430 \u0440\u0443\u0441\u0441\u043a\u043e\u043c.";
    const string Greek = "\u0393\u03b5\u03b9\u03ac \u03c3\u03bf\u03c5 \u03ba\u03cc\u03c3\u03bc\u03b5";
    const string Western = "Caf\u00e9 cr\u00e8me br\u00fbl\u00e9e \u00e0 la fran\u00e7aise";
    const string Central = "Za\u017c\u00f3\u0142\u0107 g\u0119\u015bl\u0105 ja\u017a\u0144";
    const string Turkish = "\u0130stanbul \u015fehri \u00e7ok g\u00fczel \u011f";

    // Text each encoding can hold (the round-trip sample).
    static string Sample(EncodingOption o) => o.CodePage switch
    {
        932 or 51932 or 50220 => Ja, 51949 or 949 => Ko, 950 => Hant, 936 or 54936 or 20936 => Hans,
        1251 or 28595 => Ru, 28597 => Greek, 28592 => Central, 28599 => Turkish, 1252 or 28591 or 28605 => Western,
        _ => Ja + " " + Ko + " " + Ru + " \U0001F600",
    } + "\r\nline 2 ascii\r\n";

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(15); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); }
        T Result<T>(Task<T> t) { Wait(t); return t.Result; }
        var root = IOPath.Combine(IOPath.GetTempPath(), $"mdhub-encoding-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        string F(string name, byte[] bytes) { var p = IOPath.Combine(root, name); Directory.CreateDirectory(IOPath.GetDirectoryName(p)!); File.WriteAllBytes(p, bytes); return p; }
        byte[] Bytes(EncodingOption o, string text) { var e = o.Create(); return [.. e.GetPreamble(), .. e.GetBytes(text)]; }
        var sessionPath = IOPath.Combine(IOPath.GetTempPath(), $"mdhub-encoding-{Guid.NewGuid():N}.json");
        SessionManager.PathOverride = sessionPath;
        var startLanguage = Loc.Info.Code;
        TextFileStore.AnsiCodePageOverride = 1252; // a Western system, so the CJK results below come from detection, not the ANSI code page
        var w = new MainWindow { Width = 1300, Height = 700 };
        try
        {
            Loc.Set(Loc.English);
            Check(TextEncodings.All.Select(o => o.CodePage).Distinct().Count() >= 19 && TextEncodings.All.All(o => o.Create() is not null), "All listed encodings are available (CodePagesEncodingProvider registered)");
            foreach (var name in new[] { "Shift_JIS", "EUC-JP", "ISO-2022-JP", "EUC-KR", "CP949", "Big5", "GBK", "GB18030", "GB2312", "Windows 1252", "Windows 1251", "ISO 8859-1", "ISO 8859-15", "UTF-16 LE", "UTF-16 BE", "UTF-8", "UTF-8 with BOM" })
                Check(TextEncodings.All.Any(o => o.Name == name), $"Encoding list has {name}");

            // --- Detection order: BOM, valid UTF-8, the detector, then the fallbacks ---
            (string Text, Encoding Encoding) D(byte[] bytes) => TextFileStore.Decode(bytes);
            var bomUtf8 = D(Bytes(TextEncodings.Find("utf8bom")!, Ja));
            Check(bomUtf8.Text == Ja && TextEncodings.DisplayName(bomUtf8.Encoding) == "UTF-8 with BOM", "A UTF-8 BOM is detected (UTF-8 with BOM)");
            Check(TextEncodings.DisplayName(D(Encoding.UTF8.GetBytes(Ja)).Encoding) == "UTF-8", "Valid UTF-8 without BOM is UTF-8");
            Check(TextEncodings.DisplayName(D(Encoding.ASCII.GetBytes("plain ascii")).Encoding) == "UTF-8", "Pure ASCII is UTF-8 (no guessing)");
            foreach (var (bytes, text, expected) in new (byte[], string, string)[]
            {
                (Encoding.GetEncoding(932).GetBytes(Ja), Ja, "Shift_JIS"), (Encoding.GetEncoding(51932).GetBytes(Ja), Ja, "EUC-JP"), (Encoding.GetEncoding(50220).GetBytes(Ja), Ja, "ISO-2022-JP"),
                (Encoding.GetEncoding(51949).GetBytes(Ko), Ko, "CP949"), (Encoding.GetEncoding(949).GetBytes(Ko), Ko, "CP949"),
                (Encoding.GetEncoding(950).GetBytes(Hant), Hant, "Big5"), (Encoding.GetEncoding(936).GetBytes(Hans), Hans, "GBK"), (Encoding.GetEncoding(20936).GetBytes(Hans), Hans, "GBK"),
                (Encoding.GetEncoding(1251).GetBytes(Ru), Ru, "Windows 1251"),
            })
            {
                var r = D(bytes);
                Check(r.Text == text && TextEncodings.DisplayName(r.Encoding) == expected, $"Detected {expected} ({TextEncodings.DisplayName(r.Encoding)})");
            }
            var gb18030Text = Hans + "\U00020000"; // outside GBK: only GB18030 can hold it
            var gb = D(Encoding.GetEncoding(54936).GetBytes(gb18030Text));
            Check(gb.Text == gb18030Text && gb.Encoding.CodePage == 54936, $"Text outside GBK falls through to GB18030 ({TextEncodings.DisplayName(gb.Encoding)})");
            var smart = D([.. Encoding.ASCII.GetBytes("it"), 0x92, .. Encoding.ASCII.GetBytes("s")]);
            Check(smart.Text == "it\u2019s" && smart.Encoding.CodePage == 1252, "A weak detector guess (x-mac-ce) loses to the system ANSI code page (bug 39)");

            // Low confidence: the UI language picks the CJK default; otherwise the Default encoding setting, then ANSI.
            byte[] weak = [.. Encoding.ASCII.GetBytes("it"), 0x92, .. Encoding.ASCII.GetBytes("s")];
            foreach (var (language, codePage) in new[] { (Loc.Japanese, 932), (Loc.Korean, 949), (Loc.SimplifiedChinese, 936), (Loc.TraditionalChinese, 950) })
            {
                Loc.Set(language);
                Check(TextEncodings.LegacyCandidates(weak).First().CodePage == codePage, $"{language}: low confidence falls back to code page {codePage}");
            }
            Loc.Set(Loc.English);
            Check(TextEncodings.LegacyCandidates(weak).First().CodePage == 1252, "English with Default encoding UTF-8: the system ANSI code page");
            TextEncodings.SetDefault("windows1251", persist: false);
            Check(TextEncodings.LegacyCandidates(weak).First().CodePage == 1251 && D(weak).Encoding.CodePage == 1251, "English: the Default encoding setting is the fallback");
            TextEncodings.SetDefault(TextEncodings.DefaultId, persist: false);
            Loc.Set(Loc.Japanese);
            var sjisShort = Encoding.GetEncoding(932).GetBytes("\u30c6\u30b9\u30c8");
            Check(D(sjisShort).Text == "\u30c6\u30b9\u30c8", "Japanese UI: a short Shift_JIS file opens as Shift_JIS");
            Loc.Set(Loc.English);
            Check(D([(byte)'a', 0x92, 0x81, 0xFF, (byte)'b']).Encoding is { } fallback && TextFileStore.Decode([(byte)'a', 0x81, 0x81, 0x81]).Text.Length > 0, "Undecodable bytes still open (Latin-1 or a single-byte code page)");

            // --- UTF-16 is not binary (item 52 fix: the UTF-16 check runs before the NUL check) ---
            var le = new UnicodeEncoding(false, false); var be = new UnicodeEncoding(true, false);
            var u16 = "UTF-16 text " + Ja + "\r\nsecond line\r\n";
            var leNoBom = F("utf16le-nobom", le.GetBytes(u16)); var beNoBom = F("utf16be-nobom", be.GetBytes(u16));
            var leBom = F("utf16le-bom", [0xFF, 0xFE, .. le.GetBytes(u16)]); var beBom = F("utf16be-bom", [0xFE, 0xFF, .. be.GetBytes(u16)]);
            var realBinary = F("real.dat", [(byte)'a', (byte)'b', 0, (byte)'c', 1, 2, 0, 0, 0xFF, 0x13]);
            foreach (var p in new[] { leNoBom, beNoBom, leBom, beBom })
            {
                Check(FileSupport.Detect(p) == SupportedFileKind.Text, $"{IOPath.GetFileName(p)} is text, not binary");
                Check(!WorkspaceSearch.LooksBinary(File.ReadAllBytes(p)), $"{IOPath.GetFileName(p)}: search does not skip it as binary");
            }
            Check(FileSupport.Detect(realBinary) == SupportedFileKind.Unsupported, "A real binary with NUL bytes is still binary");

            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            var status = w.GetVisualDescendants().OfType<BottomStatusBar>().Single();
            TextBlock S(string name) => status.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == name);
            void Open(string path) => Wait((Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(v, [path])!);
            NoteDocument Active() => (NoteDocument)typeof(MainView).GetField("_active", Flags)!.GetValue(v)!;
            bool Save(NoteDocument d) => Result((Task<bool>)typeof(MainView).GetMethod("SaveAsync", Flags)!.Invoke(v, [d, false])!);
            bool Reopen(NoteDocument d, string id) => Result((Task<bool>)typeof(MainView).GetMethod("ReopenWithEncodingAsync", Flags)!.Invoke(v, [d, id])!);
            bool SaveWith(NoteDocument d, string id) => Result((Task<bool>)typeof(MainView).GetMethod("SaveWithEncodingAsync", Flags)!.Invoke(v, [d, id])!);
            var confirms = new List<(string Title, string Message, string Confirm)>();
            var answer = true;
            typeof(MainView).GetProperty("ConfirmOverride", Flags)!.SetValue(v, (Func<string, string, string, Task<bool>>)((t, m, c) => { confirms.Add((t, m, c)); return Task.FromResult(answer); }));

            Open(leNoBom);
            Check(!Active().IsBinary && Active().Path == leNoBom && Active().Text == u16 && S("EncodingLabel").Text == "UTF-16 LE", "An extensionless BOM-less UTF-16 file opens as text (UTF-16 LE)");
            Open(beBom);
            Check(!Active().IsBinary && Active().Text == u16 && S("EncodingLabel").Text == "UTF-16 BE", "UTF-16 BE with BOM opens as text");

            // --- Round-trip save per encoding (Reopen with Encoding, edit, save: bytes in that encoding) ---
            foreach (var option in TextEncodings.All)
            {
                var text = Sample(option);
                var path = F($"rt/{option.Id}.txt", Bytes(option, text));
                Open(path);
                var doc = Active();
                Check(Reopen(doc, option.Id) && doc.Text == text && TextEncodings.OptionOf(doc.Encoding) == option && S("EncodingLabel").Text == option.Name, $"{option.Name}: reopens with the right text; status bar '{S("EncodingLabel").Text}'");
                Check(Save(doc) && File.ReadAllBytes(path).SequenceEqual(Bytes(option, text)), $"{option.Name}: an unchanged save writes identical bytes");
                doc.Editor.Text = text + "edited";
                Check(Save(doc) && File.ReadAllBytes(path).SequenceEqual(Bytes(option, text + "edited")), $"{option.Name}: an edited save keeps the encoding");
            }

            // --- Save keeps the detected encoding ---
            var sjisPath = F("sjis.txt", Encoding.GetEncoding(932).GetBytes(Ja));
            Open(sjisPath);
            Check(Active().Text == Ja && S("EncodingLabel").Text == "Shift_JIS" && S("FileKindLabel").Text == Loc.T("KindPlain"), "A Shift_JIS file opens detected; the status bar shows 'Plain text' and 'Shift_JIS'");
            Active().Editor.Text = Ja + "\u3002";
            Check(Save(Active()) && File.ReadAllBytes(sjisPath).SequenceEqual(Encoding.GetEncoding(932).GetBytes(Ja + "\u3002")), "Saving keeps Shift_JIS");

            // --- Status bar menu ---
            var menu = (MenuFlyout)typeof(MainView).GetMethod("BuildEncodingMenu", Flags)!.Invoke(v, [Active()])!;
            var items = menu.Items.OfType<MenuItem>().ToList();
            Check(items.Select(i => i.Name).SequenceEqual(["ReopenWithEncodingItem", "SaveWithEncodingItem"]) && (string?)items[0].Header == Loc.T("ReopenWithEncoding") && (string?)items[1].Header == Loc.T("SaveWithEncoding"), "The encoding menu offers Reopen with Encoding and Save with Encoding");
            Check(items.All(i => i.Items.Count == TextEncodings.All.Count && i.Items.OfType<MenuItem>().Count(c => c.IsChecked) == 1 && i.Items.OfType<MenuItem>().Single(c => c.IsChecked).Header as string == "Shift_JIS"), "Each submenu lists every encoding with the current one checked");
            Check(ToolTip.GetTip(S("EncodingLabel")) as string == Loc.T("EncodingMenuTip") && S("EncodingLabel").Cursor is not null, "The encoding label is clickable (hand cursor, tooltip)");

            // --- Reopen with Encoding warns about unsaved changes ---
            Active().Editor.Text = "dirty";
            confirms.Clear(); answer = false;
            Check(!Reopen(Active(), "iso88591") && Active().Text == "dirty" && confirms.Count == 1 && confirms[0].Title == "ReopenEncodingTitle" && confirms[0].Confirm == "ReopenDiscard", "Reopen with unsaved changes asks first; Cancel keeps the edits");
            answer = true;
            Check(!Reopen(Active(), "utf8") && Active().Text == "dirty", "Reopen with an encoding the file is not valid in does nothing (Shift_JIS bytes as UTF-8)");
            Check(Reopen(Active(), "shiftjis") && Active().Text == Ja + "\u3002" && !Active().IsDirty, "Confirming discards the edits and reopens");

            // --- Save with Encoding converts ---
            Check(SaveWith(Active(), "utf8") && File.ReadAllBytes(sjisPath).SequenceEqual(Encoding.UTF8.GetBytes(Ja + "\u3002")) && S("EncodingLabel").Text == "UTF-8", "Save with Encoding UTF-8 converts the file");
            Check(SaveWith(Active(), "eucjp") && File.ReadAllBytes(sjisPath).SequenceEqual(Encoding.GetEncoding(51932).GetBytes(Ja + "\u3002")) && S("EncodingLabel").Text == "EUC-JP", "Save with Encoding EUC-JP converts the file");

            // --- Unencodable characters: warn, offer UTF-8 ---
            var big5Path = F("big5.txt", Encoding.GetEncoding(950).GetBytes(Hant));
            Open(big5Path);
            var big5Doc = Active();
            Check(S("EncodingLabel").Text == "Big5", "A Big5 file opens as Big5");
            big5Doc.Editor.Text = Hant + Ko; // Hangul is not in Big5
            confirms.Clear(); answer = false;
            var before = File.ReadAllBytes(big5Path);
            Check(!Save(big5Doc) && File.ReadAllBytes(big5Path).SequenceEqual(before) && big5Doc.IsDirty && big5Doc.Encoding.CodePage == 950, "An unencodable character: declining the UTF-8 offer cancels the save, the file is untouched");
            Check(confirms.Count == 1 && confirms[0].Title == "EncodingUnencodableTitle" && confirms[0].Confirm == "SaveAsUtf8" && confirms[0].Message.Contains("Big5") && confirms[0].Message.Contains(Ko[0]), $"The warning names the encoding and the first character: {confirms.LastOrDefault().Message}");
            answer = true;
            Check(Save(big5Doc) && File.ReadAllBytes(big5Path).SequenceEqual(Encoding.UTF8.GetBytes(Hant + Ko)) && big5Doc.Encoding is UTF8Encoding && big5Doc.Encoding.GetPreamble().Length == 0 && S("EncodingLabel").Text == "UTF-8", "Accepting saves as UTF-8 (no BOM)");
            big5Doc.Editor.Text = Ko;
            answer = false; confirms.Clear();
            before = File.ReadAllBytes(big5Path);
            Check(!SaveWith(big5Doc, "shiftjis") && confirms.Count == 1 && confirms[0].Title == "EncodingUnencodableTitle", "Save with Encoding into an encoding lacking characters warns first");
            Check(big5Doc.Encoding is UTF8Encoding && File.ReadAllBytes(big5Path).SequenceEqual(before) && S("EncodingLabel").Text == "UTF-8", "Declining keeps the previous encoding and the file");

            // --- New files are UTF-8 without BOM ---
            var untitled = (NoteDocument)typeof(MainView).GetMethod("AddDocument", Flags)!.Invoke(v, [null, "new", null, null])!;
            Check(untitled.Encoding is UTF8Encoding && untitled.Encoding.GetPreamble().Length == 0, "New files are UTF-8 without BOM");
            TextEncodings.SetDefault("shiftjis", persist: false);
            var untitled2 = (NoteDocument)typeof(MainView).GetMethod("AddDocument", Flags)!.Invoke(v, [null, "new", null, null])!;
            Check(untitled2.Encoding is UTF8Encoding && untitled2.Encoding.GetPreamble().Length == 0, "New files stay UTF-8 when the Default encoding is changed");
            TextEncodings.SetDefault(TextEncodings.DefaultId, persist: false);

            // --- Search and its encoding cache ---
            var searchRoot = IOPath.Combine(root, "search"); Directory.CreateDirectory(searchRoot);
            File.WriteAllBytes(IOPath.Combine(searchRoot, "ja.txt"), Encoding.GetEncoding(932).GetBytes("memo: " + Ja));
            File.WriteAllBytes(IOPath.Combine(searchRoot, "ko.txt"), Encoding.GetEncoding(949).GetBytes("memo: " + Ko));
            File.WriteAllBytes(IOPath.Combine(searchRoot, "hans.txt"), Encoding.GetEncoding(936).GetBytes("memo: " + Hans));
            File.WriteAllBytes(IOPath.Combine(searchRoot, "hant.txt"), Encoding.GetEncoding(950).GetBytes("memo: " + Hant));
            File.WriteAllBytes(IOPath.Combine(searchRoot, "u16"), le.GetBytes("memo: this note is UTF-16 LE without a BOM, mostly ASCII. " + Ja)); // BOM-less UTF-16 is recognised by its NUL high bytes
            var options = new FindOptions(false, false, false, false);
            WorkspaceSearchResult Search(string q) => WorkspaceSearch.SearchAsync(searchRoot, q, options, new Dictionary<string, string>(), CancellationToken.None).GetAwaiter().GetResult();
            TextEncodings.ClearCache();
            foreach (var (query, file, encoding) in new[] { ("\u65e5\u672c\u8a9e", "ja.txt", "Shift_JIS"), ("\ud55c\uad6d\uc5b4", "ko.txt", "CP949"), ("\u7b80\u4f53", "hans.txt", "GBK"), ("\u7e41\u9ad4", "hant.txt", "Big5") })
            {
                var r = Search(query);
                Check(r.Files.Any(f => IOPath.GetFileName(f.Path) == file && TextEncodings.DisplayName(f.Encoding) == encoding && !f.Lossy && f.Hits.Count == 1) && r.Lossy == 0, $"Search finds '{query}' in the {encoding} file");
            }
            Check(Search("\u4e16\u754c").Files.Select(f => IOPath.GetFileName(f.Path)).Order().SequenceEqual(new[] { "hans.txt", "hant.txt", "ja.txt", "u16" }), "A CJK query matches every legacy and UTF-16 file containing it: " + string.Join(",", Search("\u4e16\u754c").Files.Select(f => IOPath.GetFileName(f.Path))));
            var hits = TextEncodings.CacheHits;
            Search("memo");
            Check(TextEncodings.CacheHits >= hits + 5, $"A repeated search reuses the cached encodings ({TextEncodings.CacheHits - hits} hits)");
            var jaPath = IOPath.Combine(searchRoot, "ja.txt");
            File.WriteAllBytes(jaPath, Encoding.GetEncoding(51932).GetBytes("memo: " + Ja + " \u66f4\u65b0"));
            File.SetLastWriteTimeUtc(jaPath, DateTime.UtcNow.AddMinutes(1));
            var updated = Search("\u66f4\u65b0").Files.SingleOrDefault();
            Check(updated is not null && TextEncodings.DisplayName(updated.Encoding) == "EUC-JP", "A changed file (new mtime) is detected again");
            var quickOpen = QuickOpenIndex.Enumerate(searchRoot, 100, out _);
            Check(quickOpen.Contains("ja.txt") && quickOpen.Contains("u16"), "Quick Open lists legacy and UTF-16 files (it matches names only, so it needs no decoding)");

            // --- Default encoding setting (editor gear) ---
            SessionManager.Enabled = true;
            var panel = EditorFontsFlyout.BuildEncoding();
            var box = panel.Children.OfType<ComboBox>().Single(c => c.Name == "DefaultEncodingBox");
            Check(box.SelectedItem as string == "UTF-8" && panel.Children.OfType<TextBlock>().Any(t => t.Text == Loc.T("DefaultEncoding")), "The Default encoding setting defaults to UTF-8");
            box.SelectedItem = "Windows 1251";
            Check(TextEncodings.DefaultEncodingId == "windows1251" && SessionStore.Load(sessionPath)?.DefaultEncoding == "windows1251", "Picking an encoding saves it in the session");
            TextEncodings.ImportDefault(null);
            Check(TextEncodings.DefaultEncodingId == "utf8", "Missing in an old session: UTF-8");
            TextEncodings.ImportDefault("windows1251");
            Check(TextEncodings.DefaultEncodingId == "windows1251", "The saved setting is restored");
            TextEncodings.SetDefault(TextEncodings.DefaultId);
            Check(SessionStore.Load(sessionPath)?.DefaultEncoding is null, "UTF-8 is not written (null in the session)");

            // --- Localization and theme ---
            string[] keys = ["EncodingMenuTip", "ReopenWithEncoding", "SaveWithEncoding", "ReopenEncodingTitle", "ReopenEncodingConfirm", "ReopenDiscard", "ReopenEncodingFailed", "ReopenedEncoding", "EncodingUnencodableTitle", "EncodingUnencodable", "SaveAsUtf8", "EncodingSettings", "DefaultEncoding", "DefaultEncodingTip"];
            foreach (var code in Loc.Languages.Select(l => l.Code))
            {
                var table = Loc.Table(code);
                Check(keys.All(k => table.TryGetValue(k, out var s) && !string.IsNullOrWhiteSpace(s) && (code == Loc.English || s != Loc.Table(Loc.English)[k])), $"{code}: all item 67 strings are translated");
                Check(string.Format(table["EncodingUnencodable"], "X", "Y").Contains('X') && string.Format(table["EncodingUnencodable"], "X", "Y").Contains('Y') && string.Format(table["ReopenEncodingFailed"], "X").Contains('X'), $"{code}: placeholders are kept");
            }
            var xaml = File.ReadAllText(IOPath.Combine(Repo(), "Components", "BottomStatusBar.axaml"));
            Check(!System.Text.RegularExpressions.Regex.IsMatch(xaml, "#[0-9A-Fa-f]{6}") && xaml.Contains("x:Name=\"EncodingLabel\"") && xaml.Contains("{DynamicResource Accent}"), "The encoding label uses theme resources only");
            // --- Item 81: mdhub's own detector (no UTF.Unknown) ---
            Loc.Set(Loc.English);
            var misses = new List<string>();
            void Soft(bool ok, string name) { if (ok) Check(true, name); else { misses.Add(name); Console.WriteLine("MISS " + name); } }
            string Name(byte[] bytes) => TextEncodings.DisplayName(D(bytes).Encoding);
            var notesHant = new[]
            {
                "\u6703\u8b70\u7d00\u9304\uff1a\u4e0b\u9031\u4e09\u4e0b\u5348\u5169\u9ede\u8207\u8a2d\u8a08\u5718\u968a\u8a0e\u8ad6\u65b0\u7248\u9996\u9801\uff0c\u8acb\u8a18\u5f97\u5e36\u7b46\u96fb\u3002",
                "\u5f85\u8fa6\u4e8b\u9805\uff1a\u6574\u7406\u8cc7\u6599\u593e\u3001\u56de\u8986\u5ba2\u6236\u90f5\u4ef6\u3001\u78ba\u8a8d\u767c\u7968\u91d1\u984d\u662f\u5426\u6b63\u78ba\u3002",
                "\u8b80\u66f8\u7b46\u8a18\uff1a\u9019\u672c\u66f8\u8ac7\u5230\u6642\u9593\u7ba1\u7406\u7684\u91cd\u8981\u6027\uff0c\u4f5c\u8005\u8a8d\u70ba\u61c9\u8a72\u5148\u8655\u7406\u6700\u56f0\u96e3\u7684\u5de5\u4f5c\u3002",
                "\u65c5\u884c\u8a08\u756b\uff1a\u81fa\u5317\u5230\u9ad8\u96c4\u642d\u9ad8\u9435\uff0c\u9810\u8a08\u9031\u672b\u51fa\u767c\uff0c\u8a18\u5f97\u8a02\u98ef\u5e97\u3002",
                "# \u5de5\u4f5c\u65e5\u8a8c\n\n- \u4fee\u6b63\u767b\u5165\u9801\u9762\u7684\u932f\u8aa4\n- \u6e2c\u8a66\u65b0\u529f\u80fd\n",
            };
            var gbkNote = "\u4f1a\u8bae\u8bb0\u5f55\uff1a\u4e0b\u5468\u4e09\u4e0b\u5348\u4e24\u70b9\u4e0e\u8bbe\u8ba1\u56e2\u961f\u8ba8\u8bba\u65b0\u7248\u9996\u9875\uff0c\u8bf7\u8bb0\u5f97\u5e26\u7b14\u8bb0\u672c\u7535\u8111\u3002";
            var sjisNote = "\u4f1a\u8b70\u306e\u8b70\u4e8b\u9332\uff1a\u6765\u9031\u306e\u6c34\u66dc\u65e5\u306e\u5348\u5f8c\u4e8c\u6642\u304b\u3089\u3001\u30c7\u30b6\u30a4\u30f3\u30c1\u30fc\u30e0\u3068\u65b0\u3057\u3044\u30db\u30fc\u30e0\u30da\u30fc\u30b8\u306b\u3064\u3044\u3066\u8a71\u3057\u5408\u3044\u307e\u3059\u3002";
            var korNote = "\ud68c\uc758\ub85d: \ub2e4\uc74c \uc8fc \uc218\uc694\uc77c \uc624\ud6c4 \ub450 \uc2dc\uc5d0 \ub514\uc790\uc778 \ud300\uacfc \uc0c8 \ud648\ud398\uc774\uc9c0\uc5d0 \ub300\ud574 \ub17c\uc758\ud569\ub2c8\ub2e4.";
            foreach (var language in new[] { Loc.English, Loc.Japanese, Loc.Korean, Loc.SimplifiedChinese, Loc.TraditionalChinese })
            {
                Loc.Set(language);
                for (var i = 0; i < notesHant.Length; i++)
                {
                    var r = D(Encoding.GetEncoding(950).GetBytes(notesHant[i]));
                    Soft(r.Text == notesHant[i] && r.Encoding.CodePage == 950, $"Item 81 ({language}): Big5 note {i + 1} is Big5 ({TextEncodings.DisplayName(r.Encoding)})");
                }
                foreach (var (text, cp, expected) in new[] { (gbkNote, 936, "GBK"), (sjisNote, 932, "Shift_JIS"), (korNote, 51949, "CP949"), (korNote, 949, "CP949"), (sjisNote, 51932, "EUC-JP") })
                {
                    var r = D(Encoding.GetEncoding(cp).GetBytes(text));
                    Soft(r.Text == text && TextEncodings.DisplayName(r.Encoding) == expected, $"Item 81 ({language}): code page {cp} note is {expected} ({TextEncodings.DisplayName(r.Encoding)})");
                }
            }
            Loc.Set(Loc.English);
            Soft(Name(Encoding.UTF8.GetBytes(notesHant[0])) == "UTF-8", "Item 81: Chinese UTF-8 without BOM is UTF-8");
            Soft(Name([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(notesHant[0])]) == "UTF-8 with BOM", "Item 81: Chinese UTF-8 with BOM is UTF-8 with BOM");
            var le81 = D(Encoding.Unicode.GetBytes("Plain notes for the week, nothing special here."));
            Soft(le81.Encoding.CodePage == 1200 && le81.Text.StartsWith("Plain notes"), "Item 81: BOM-less UTF-16 LE is UTF-16 LE");
            var be81 = D(Encoding.BigEndianUnicode.GetBytes("Plain notes for the week, nothing special here."));
            Soft(be81.Encoding.CodePage == 1201 && be81.Text.StartsWith("Plain notes"), "Item 81: BOM-less UTF-16 BE is UTF-16 BE");
            Soft(Name(Encoding.ASCII.GetBytes("ok")) == "UTF-8", "Item 81: short ASCII is UTF-8");
            var binary = Enumerable.Range(0, 4096).Select(i => (byte)(i * 7919 % 256)).ToArray();
            Soft(WorkspaceSearch.LooksBinary(binary), "Item 81: random bytes still count as binary (item 52's check runs before detection)");
            Soft(D(binary).Text.Length > 0, "Item 81: decoding binary bytes never throws");
            Soft(!EncodingDetector.IsConfident(EncodingDetector.Rank(binary, null)), "Item 81: the detector is not confident about binary bytes");
            // Short ambiguous: E0 A1 E0 A1 is a valid double-byte pair twice in Big5, GBK, Shift_JIS and CP949; the UI language decides.
            byte[] ambiguous = [0xE0, 0xA1, 0xE0, 0xA1];
            foreach (var cp in new[] { 950, 936, 932, 949 }) Soft(TextEncodings.TryDecode(TextEncodings.Strict(cp)!, ambiguous, out _), $"Item 81: the ambiguous sample is valid in code page {cp}");
            foreach (var (language, codePage) in new[] { (Loc.TraditionalChinese, 950), (Loc.SimplifiedChinese, 936), (Loc.Japanese, 932), (Loc.Korean, 949) })
            {
                Loc.Set(language);
                var r = D(ambiguous);
                Soft(r.Encoding.CodePage == codePage, $"Item 81 ({language}): short ambiguous bytes use code page {codePage} ({TextEncodings.DisplayName(r.Encoding)})");
            }
            Loc.Set(Loc.TraditionalChinese);
            Soft(D(Encoding.GetEncoding(950).GetBytes("\u4e2d\u6587")).Text == "\u4e2d\u6587", "Item 81 (zh-Hant): short Big5 \u4e2d\u6587 round-trips");
            Loc.Set(Loc.English);
            Soft(D(ambiguous).Text.Length > 0, "Item 81 (English): short ambiguous bytes still open");
            Check(misses.Count == 0, $"Item 81: no misdetections ({misses.Count}: {string.Join("; ", misses)})");
            Console.WriteLine($"Encoding tests: {passed} passed");
        }
        finally
        {
            TextFileStore.AnsiCodePageOverride = null;
            TextEncodings.ImportDefault(null);
            SessionManager.Enabled = false; SessionManager.PathOverride = null;
            Loc.Set(startLanguage);
            w.Close();
            try { Directory.Delete(root, true); } catch { }
            try { File.Delete(sessionPath); } catch { }
        }
    }

    static string Repo([System.Runtime.CompilerServices.CallerFilePath] string path = "") => IOPath.GetFullPath(IOPath.Combine(IOPath.GetDirectoryName(path)!, "..", ".."));
}