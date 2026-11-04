using System.Text;

namespace Mdhub.Services;

// Stored only in recovery.json, separate from the lightweight saved-file session.
public sealed record RecoveryDocument
{
    public string Key { get; init; } = "";
    public string? Path { get; init; }
    public string Name { get; init; } = "Untitled.md";
    public string Text { get; init; } = "";
    public string SavedText { get; init; } = "";
    public int CodePage { get; init; } = 65001;
    public int? SavedCodePage { get; init; }
    public bool? SavedBom { get; init; }
    public bool Bom { get; init; }
    public int LineEnding { get; init; }
    public int SavedLineEnding { get; init; }

    public Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return CodePage switch
        {
            65001 => new UTF8Encoding(Bom, true),
            1200 => new UnicodeEncoding(false, Bom, true),
            1201 => new UnicodeEncoding(true, Bom, true),
            12000 => new UTF32Encoding(false, Bom, true),
            12001 => new UTF32Encoding(true, Bom, true),
            _ => Encoding.GetEncoding(CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
        };
    }
}
