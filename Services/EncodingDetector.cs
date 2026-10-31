using System.Text;

namespace Mdhub.Services;

// Item 81: mdhub's own legacy-encoding heuristic (replaces UTF.Unknown). It runs after BOM, BOM-less UTF-16, the
// binary check, ISO-2022-JP and strict UTF-8 have failed. Each candidate decodes a sample through
// CodePagesEncodingProvider: an invalid lead/trail byte sequence rules it out, then the decoded characters are scored
// (common Hanzi / kana / Hangul / Cyrillic words / accented Latin words score high, private-use, control, half-width
// katakana and stray symbols score low), normalised per non-ASCII byte. A clear winner is "confident"; otherwise the
// caller's language / Default encoding / ANSI fallbacks come first. Ties break by UI language.
public static class EncodingDetector
{
    // Big5 (950), GBK (936), GB18030 (54936), Shift_JIS (932), EUC-JP (51932), EUC-KR as its superset CP949 (949),
    // Windows-1251 (kept so Cyrillic files still open as before) and Windows-1252.
    public static readonly int[] Candidates = [950, 936, 54936, 932, 51932, 949, 1251, 1252];

    // Small built-in frequency sets: common characters of everyday notes.
    const string CommonTraditional = "的一是不了人我在有他這中大來上個國和也子時道出而要於就下得可你年生自會那後能對著事其裡所去行過家十用發天如然作方成者多日都三小軍二無同麼經法當起與好看學進種將還分此心前面又定見只主沒公從知全己部長話問很最重新已現想開明實力外它間第因做工資電文體理物高月加回本機動品樣業期點女給相信通身但真意情把條件市場系統使料檔案筆記內容今錄議專設程式計題需應該們什那這各位請謝更改完成測試版務客戶報告週末早晚吃飯買東西錢";
    const string CommonSimplified = "的一是不了人我在有他这中大来上个国和也子时道出而要于就下得可你年生自会那后能对着事其里所去行过家十用发天如然作方成者多日都三小军二无同么经法当起与好看学进种将还分此心前面又定见只主没公从知全己部长话问很最重新已现想开明实力外它间第因做工资电文体理物高月加回本机动品样业期点女给相信通身但真意情把条件市场系统使料档案笔记内容今录议项设程序计题需应该们什那这各位请谢更改完成测试版务客户报告周末早晚吃饭买东西钱";
    const string CommonKanji = "日本語人年月時間今行見出入大小中上下前後会社事業学生先私何方気手自分思言話新使用記録議題確認予定作成変更設定対応問題必要場合東京円明朝夜週末食物買";
    const string CommonHangul = "이다는의에가을를고하지한그기서도로으사리어나자대것수아해시들게일보요있없만내니정주인우상회록프젝트설작업확필문제오늘다음간관련용경및등위통께합니까세안녕감했습";
    static readonly HashSet<char> Traditional = [.. CommonTraditional], Simplified = [.. CommonSimplified], Kanji = [.. CommonKanji, .. CommonTraditional.Intersect(CommonSimplified)], Hangul = [.. CommonHangul];

    public readonly record struct Candidate(int CodePage, double Score, int NonAsciiBytes);

    // Scores every candidate that decodes the sample (64 KB at most); best first, UI-language tie-break applied.
    public static List<Candidate> Rank(ReadOnlySpan<byte> bytes, int? languageCodePage)
    {
        var sample = bytes[..Math.Min(bytes.Length, 64 * 1024)];
        var nonAscii = 0;
        foreach (var b in sample) if (b >= 0x80) nonAscii++;
        var ranked = new List<Candidate>();
        if (nonAscii == 0) return ranked;
        foreach (var cp in Candidates)
            if (TextEncodings.Strict(cp) is { } encoding && DecodeSample(encoding, sample) is { } text)
                ranked.Add(new(cp, Score(text, cp) / nonAscii, nonAscii));
        ranked.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (languageCodePage is { } preferred && ranked.Count > 1 && ranked.FindIndex(c => c.CodePage == preferred) is var i and > 0
            && ranked[0].Score - ranked[i].Score <= TieMargin)
        {
            var pick = ranked[i]; ranked.RemoveAt(i); ranked.Insert(0, pick);
        }
        return ranked;
    }

    public const double TieMargin = 0.1;
    public const double ConfidentScore = 0.35;
    public const int ConfidentBytes = 6;

    // Confident: enough non-ASCII bytes and a good score (short or odd samples defer to the language fallbacks).
    public static bool IsConfident(List<Candidate> ranked) =>
        ranked.Count > 0 && ranked[0].NonAsciiBytes >= ConfidentBytes && ranked[0].Score >= ConfidentScore;

    // Strict decode of a sample that may end inside a multi-byte sequence (flush: false keeps a split tail pending).
    static string? DecodeSample(Encoding encoding, ReadOnlySpan<byte> bytes)
    {
        var probe = (Encoding)encoding.Clone();
        var flag = new Flag();
        probe.DecoderFallback = flag;
        var decoder = probe.GetDecoder();
        var chars = new char[probe.GetMaxCharCount(bytes.Length)];
        var count = decoder.GetChars(bytes, chars, flush: false);
        return flag.Failed ? null : new string(chars, 0, count);
    }

    static double Score(string text, int cp)
    {
        bool japanese = cp is 932 or 51932, korean = cp == 949;
        var common = cp == 950 ? Traditional : cp is 936 or 54936 ? Simplified : japanese ? Kanji : korean ? Hangul : null;
        double score = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c < 0x80) { if (char.IsControl(c) && c is not ('\t' or '\n' or '\r' or '\f' or '\u001b')) score -= 3; continue; } // NUL and friends: binary, not text
            if (c == '\uFFFD' || c is >= '\uE000' and <= '\uF8FF' || char.IsControl(c)) { score -= 3; continue; }
            if (common is not null)
            {
                if (common.Contains(c)) score += 3;
                else if (c is >= '\u3041' and <= '\u30FA') score += japanese ? 3 : -1;              // hiragana, katakana
                else if (c is >= '\uAC00' and <= '\uD7A3') score += korean ? 1 : -1;                 // Hangul syllables
                else if (c is >= '\u4E00' and <= '\u9FFF') score += korean ? -0.5 : 0.5;            // other ideographs
                else if (c is >= '\u3000' and <= '\u303F' or >= '\uFF01' and <= '\uFF5E') score += 1; // CJK / full-width punctuation
                else if (c is >= '\uFF61' and <= '\uFF9F') score -= 1;                               // half-width katakana
                else score -= 0.5;                                                                   // box drawing, Greek, Cyrillic rows
                continue;
            }
            bool Near(Func<char, bool> test) => i > 0 && test(text[i - 1]) || i + 1 < text.Length && test(text[i + 1]);
            if (cp == 1251)
            {
                if (c is >= '\u0400' and <= '\u04FF') score += Near(n => n is >= '\u0400' and <= '\u04FF') ? 1 : 0.2;
                else score += c is '\u00AB' or '\u00BB' or '\u2014' or '\u2013' or '\u2026' or '\u2116' ? 0.5 : -0.5;
            }
            else
            {
                if (char.IsLetter(c) && c < '\u0250') score += Near(n => n < 0x80 && char.IsAsciiLetter(n)) ? 1 : 0.2;
                else score += c is '\u2018' or '\u2019' or '\u201C' or '\u201D' or '\u2013' or '\u2014' or '\u2026' or '\u20AC' or '\u00A0' or '\u00B0' or '\u00A9' ? 0.8 : -0.5;
            }
        }
        return score;
    }

    private sealed class Flag : DecoderFallback
    {
        public bool Failed;
        public override int MaxCharCount => 1;
        public override DecoderFallbackBuffer CreateFallbackBuffer() => new Buffer(this);
        private sealed class Buffer(Flag owner) : DecoderFallbackBuffer
        {
            private bool _pending;
            public override bool Fallback(byte[] bytesUnknown, int index) { owner.Failed = true; _pending = true; return true; }
            public override char GetNextChar() { if (!_pending) return '\0'; _pending = false; return '\uFFFD'; }
            public override bool MovePrevious() => false;
            public override int Remaining => _pending ? 1 : 0;
        }
    }
}