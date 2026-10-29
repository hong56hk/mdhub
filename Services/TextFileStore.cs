using System.Text;

namespace Mdhub.Services;

// No UI dependencies: the filesystem behavior can be tested independently.
public static class TextFileStore
{
    public static Encoding DefaultEncoding => new UTF8Encoding(false, true);

    public static async Task<(string Text, Encoding Encoding)> ReadAsync(string path)
    {
        using var reader = new StreamReader(path, DefaultEncoding, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync();
        return (text, reader.CurrentEncoding);
    }

    public static async Task WriteAsync(string path, string text, Encoding encoding)
    {
        // Write beside the destination, then replace it. A failed write must not truncate the original.
        var fullPath = Path.GetFullPath(path);
        var temporaryPath = Path.Combine(Path.GetDirectoryName(fullPath)!, $".mdhub-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, text, encoding);
            if (File.Exists(fullPath))
                File.Replace(temporaryPath, fullPath, null);
            else
                File.Move(temporaryPath, fullPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
