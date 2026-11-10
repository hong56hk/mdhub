using Docnet.Core;
using Docnet.Core.Exceptions;
using Docnet.Core.Models;
using Docnet.Core.Readers;

namespace Mdhub.Services;

// Item 102a: a PDFium document (Docnet.Core) opened for the viewer. The same file may be shown in more than one
// split, so a lock covers every native call. Encrypted and corrupt files do not throw: Error is set instead.
public enum PdfError { None, Encrypted, Corrupt }

public sealed class PdfDocument : IDisposable
{
    // PDFium is process-wide and not safe across documents without a lock.
    static readonly object Pdfium = new();
    readonly string _path;
    IDocReader? _reader;
    double _readerScale;
    bool _disposed;

    public PdfError Error { get; }
    public int PageCount { get; }
    // Page size at 100% (1 PDF point = 1 DIP).
    public IReadOnlyList<(double Width, double Height)> Pages { get; }

    PdfDocument(string path, PdfError error, IDocReader? reader, IReadOnlyList<(double, double)> pages)
    {
        _path = path;
        Error = error;
        _reader = reader;
        _readerScale = error == PdfError.None ? 1 : 0;
        PageCount = pages.Count;
        Pages = pages;
    }

    public static PdfDocument Open(string path)
    {
        path = Path.GetFullPath(path);
        lock (Pdfium)
        {
            try
            {
                var reader = DocLib.Instance.GetDocReader(path, new PageDimensions(1.0));
                var count = reader.GetPageCount();
                if (count <= 0) { reader.Dispose(); return new PdfDocument(path, PdfError.Corrupt, null, []); }
                var pages = new (double, double)[count];
                for (var i = 0; i < count; i++)
                {
                    using var page = reader.GetPageReader(i);
                    pages[i] = (page.GetPageWidth(), page.GetPageHeight());
                }
                return new PdfDocument(path, PdfError.None, reader, pages);
            }
            catch (DocnetLoadDocumentException ex)
            {
                return new PdfDocument(path, IsPassword(ex) ? PdfError.Encrypted : PdfError.Corrupt, null, []);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DocnetException)
            {
                return new PdfDocument(path, PdfError.Corrupt, null, []);
            }
        }
    }

    public static bool IsPassword(DocnetLoadDocumentException ex) =>
        ex.Message.Contains("ErrorCode=4") || ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase);

    public (int Width, int Height, byte[] Bgra)? Render(int index, double scale)
    {
        if (Error != PdfError.None || index < 0 || index >= PageCount) return null;
        scale = ImageZoom.Clamp(scale);
        lock (Pdfium)
        {
            EnsureReader(scale);
            if (_reader is null) return null;
            using var page = _reader.GetPageReader(index);
            var w = page.GetPageWidth();
            var h = page.GetPageHeight();
            var bgra = page.GetImage();
            if (w <= 0 || h <= 0 || bgra.Length < w * h * 4) return null;
            return (w, h, bgra);
        }
    }

    // Ready for item 102b (text layer / selection).
    public string Text(int index)
    {
        if (Error != PdfError.None || index < 0 || index >= PageCount) return "";
        lock (Pdfium)
        {
            EnsureReader(_readerScale > 0 ? _readerScale : 1);
            if (_reader is null) return "";
            using var page = _reader.GetPageReader(index);
            return page.GetText() ?? "";
        }
    }

    void EnsureReader(double scale)
    {
        if (_reader is not null && Math.Abs(_readerScale - scale) < 1e-6) return;
        _reader?.Dispose();
        _reader = null;
        _readerScale = 0;
        _reader = DocLib.Instance.GetDocReader(_path, new PageDimensions(scale));
        _readerScale = scale;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (Pdfium)
        {
            _reader?.Dispose();
            _reader = null;
        }
    }
}