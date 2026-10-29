using System.Globalization;
using System.Text.RegularExpressions;

namespace Mdhub.Text;

public readonly record struct TextStatistics(int Words, int Characters, int Lines)
{
    // Words are whitespace-delimited; characters are Unicode text elements, including whitespace.
    public static TextStatistics Count(string text) => new(
        Regex.Matches(text, @"\S+").Count,
        new StringInfo(text).LengthInTextElements,
        Regex.Matches(text, @"\r\n|\r|\n").Count + 1);
}
