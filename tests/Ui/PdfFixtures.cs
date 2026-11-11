using System.Security.Cryptography;
using System.Text;

// Item 102a: small PDF fixtures generated in-process (no pre-built binaries in the repo).
internal static class PdfFixtures
{
    static readonly byte[] Pad = [0x28,0xBF,0x4E,0x5E,0x4E,0x75,0x8A,0x41,0x64,0x00,0x4E,0x56,0xFF,0xFA,0x01,0x08,0x2E,0x2E,0x00,0xB6,0xD0,0x68,0x3E,0x80,0x2F,0x0C,0xA9,0xFE,0x64,0x53,0x69,0x7A];

    public static byte[] MultiPage(params string[] texts)
    {
        var objs = new List<string> { "" };
        objs.Add("<< /Type /Catalog /Pages 2 0 R >>");
        objs.Add("PLACEHOLDER_PAGES");
        objs.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        var kids = new List<int>();
        for (var i = 0; i < texts.Length; i++)
        {
            var pageId = objs.Count;
            var contentId = pageId + 1;
            kids.Add(pageId);
            var s = "BT /F1 18 Tf 50 700 Td (" + Esc(texts[i]) + ") Tj ET";
            objs.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {contentId} 0 R /Resources << /Font << /F1 3 0 R >> >> >>");
            objs.Add($"<< /Length {s.Length} >> stream\n{s}\nendstream");
        }
        objs[2] = "<< /Type /Pages /Kids [" + string.Join(" ", kids.Select(k => k + " 0 R")) + "] /Count " + texts.Length + " >>";
        return Assemble(objs, encrypt: null, id: null);
    }

    // Custom MediaBox for layout tests (item 105). MultiPage stays letter-size.
    public static byte[] Pages(int width, int height, params string[] texts)
    {
        var objs = new List<string> { "" };
        objs.Add("<< /Type /Catalog /Pages 2 0 R >>");
        objs.Add("PLACEHOLDER_PAGES");
        objs.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        var kids = new List<int>();
        for (var i = 0; i < texts.Length; i++)
        {
            var pageId = objs.Count;
            var contentId = pageId + 1;
            kids.Add(pageId);
            var s = "BT /F1 18 Tf 50 50 Td (" + Esc(texts[i]) + ") Tj ET";
            objs.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Contents {contentId} 0 R /Resources << /Font << /F1 3 0 R >> >> >>");
            objs.Add($"<< /Length {s.Length} >> stream\n{s}\nendstream");
        }
        objs[2] = "<< /Type /Pages /Kids [" + string.Join(" ", kids.Select(k => k + " 0 R")) + "] /Count " + texts.Length + " >>";
        return Assemble(objs, encrypt: null, id: null);
    }

    public static byte[] Corrupt() => Encoding.ASCII.GetBytes("%PDF-1.4\nthis is not a pdf\n%%EOF\n");

    // Standard security V=1 R=2 (40-bit RC4), user password "test".
    public static byte[] Encrypted(string text, string userPassword = "test")
    {
        byte[] PadPw(string pw)
        {
            var b = Encoding.Latin1.GetBytes(pw);
            var r = new byte[32];
            var n = Math.Min(32, b.Length);
            Buffer.BlockCopy(b, 0, r, 0, n);
            Buffer.BlockCopy(Pad, 0, r, n, 32 - n);
            return r;
        }
        byte[] Rc4(byte[] key, byte[] data)
        {
            var s = new byte[256];
            for (var i = 0; i < 256; i++) s[i] = (byte)i;
            var j = 0;
            for (var i = 0; i < 256; i++) { j = (j + s[i] + key[i % key.Length]) & 255; (s[i], s[j]) = (s[j], s[i]); }
            var outp = new byte[data.Length];
            int x = 0, y = 0;
            for (var i = 0; i < data.Length; i++)
            {
                x = (x + 1) & 255; y = (y + s[x]) & 255; (s[x], s[y]) = (s[y], s[x]);
                outp[i] = (byte)(data[i] ^ s[(s[x] + s[y]) & 255]);
            }
            return outp;
        }
        var id = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        const int Pperm = unchecked((int)0xFFFFFFFC);
        var o = Rc4(MD5.HashData(PadPw("owner"))[..5], PadPw(userPassword));
        var key = MD5.HashData(PadPw(userPassword).Concat(o).Concat(BitConverter.GetBytes(Pperm)).Concat(id).ToArray())[..5];
        var u = Rc4(key, Pad);
        byte[] ObjKey(int obj)
        {
            var extra = new byte[key.Length + 5];
            Buffer.BlockCopy(key, 0, extra, 0, key.Length);
            extra[key.Length] = (byte)(obj & 255);
            extra[key.Length + 1] = (byte)((obj >> 8) & 255);
            extra[key.Length + 2] = (byte)((obj >> 16) & 255);
            return MD5.HashData(extra)[..10];
        }
        var content = Encoding.ASCII.GetBytes("BT /F1 18 Tf 50 700 Td (" + Esc(text) + ") Tj ET");
        var encContent = Rc4(ObjKey(5), content);
        var encStream = "<< /Length " + encContent.Length + " >> stream\n" + Encoding.Latin1.GetString(encContent) + "\nendstream";
        var objs = new List<string> { "",
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [4 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 5 0 R /Resources << /Font << /F1 3 0 R >> >> >>",
            encStream,
            "<< /Filter /Standard /V 1 /R 2 /O <" + Convert.ToHexString(o) + "> /U <" + Convert.ToHexString(u) + "> /P " + Pperm + " >>"
        };
        return Assemble(objs, (6, Convert.ToHexString(id)), id);
    }

    static string Esc(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    static byte[] Assemble(List<string> objs, (int Obj, string IdHex)? encrypt, byte[]? id)
    {
        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
        var offsets = new int[objs.Count];
        for (var i = 1; i < objs.Count; i++)
        {
            offsets[i] = Encoding.ASCII.GetByteCount(sb.ToString());
            sb.Append(i).Append(" 0 obj\n").Append(objs[i]).Append("\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append("xref\n0 ").Append(objs.Count).Append('\n');
        sb.Append("0000000000 65535 f \n");
        for (var i = 1; i < objs.Count; i++) sb.Append(offsets[i].ToString("D10")).Append(" 00000 n \n");
        sb.Append("trailer << /Size ").Append(objs.Count).Append(" /Root 1 0 R");
        if (encrypt is { } enc) sb.Append(" /Encrypt ").Append(enc.Obj).Append(" 0 R /ID [<").Append(enc.IdHex).Append("><").Append(enc.IdHex).Append(">]");
        sb.Append(" >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }
}