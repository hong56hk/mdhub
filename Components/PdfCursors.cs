using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;

namespace Mdhub.Components;

// Item 109: a generated open-hand cursor (five fingers up, palm) used while panning a PDF.
// Cached, drawn in code (white fill, thin black outline) so it reads on light and dark backgrounds.
// No third-party asset and no change to the existing icon set.
public static class PdfCursors
{
    public const int Size = 32;
    public const int SizeHighDpi = 64;

    static Cursor? _standard, _highDpi;

    public static Cursor OpenHand(double scaling = 1) =>
        scaling >= 1.5 ? (_highDpi ??= Create(SizeHighDpi)) : (_standard ??= Create(Size));

    static Cursor Create(int size)
    {
        var bitmap = Draw(size);
        var hotspot = Hotspot(size);
        return new Cursor(bitmap, hotspot);
    }

    public static PixelPoint Hotspot(int size)
    {
        // Palm centre of the 32x32 silhouette, scaled.
        var scale = size / (double)Size;
        return new PixelPoint((int)Math.Round(17 * scale), (int)Math.Round(20 * scale));
    }

    static WriteableBitmap Draw(int size)
    {
        var fill = new bool[size, size];
        Paint(fill, size);
        var bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = bitmap.Lock();
        var stride = fb.RowBytes;
        var data = new byte[stride * size];
        var radius = size >= SizeHighDpi ? 2 : 1;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var i = y * stride + x * 4;
                if (fill[x, y])
                {
                    // White, premultiplied.
                    data[i] = 255;
                    data[i + 1] = 255;
                    data[i + 2] = 255;
                    data[i + 3] = 255;
                }
                else if (NearFill(fill, size, x, y, radius))
                {
                    // Black outline.
                    data[i] = 0;
                    data[i + 1] = 0;
                    data[i + 2] = 0;
                    data[i + 3] = 255;
                }
            }
        }
        Marshal.Copy(data, 0, fb.Address, data.Length);
        return bitmap;
    }

    static void Paint(bool[,] fill, int size)
    {
        var s = size / (double)Size;
        void Rect(double x0, double y0, double x1, double y1)
        {
            var left = Math.Max(0, (int)Math.Round(x0 * s));
            var top = Math.Max(0, (int)Math.Round(y0 * s));
            var right = Math.Min(size - 1, (int)Math.Round(x1 * s));
            var bottom = Math.Min(size - 1, (int)Math.Round(y1 * s));
            for (var y = top; y <= bottom; y++)
                for (var x = left; x <= right; x++)
                    fill[x, y] = true;
        }

        // Four fingers up (middle tallest) and a thumb raised on the left.
        Rect(8, 5, 10, 16);   // index
        Rect(12, 2, 14, 16);  // middle
        Rect(16, 3, 18, 16);  // ring
        Rect(20, 6, 22, 16);  // pinky
        Rect(3, 12, 8, 18);   // thumb (out and slightly up)
        Rect(3, 11, 6, 16);
        Rect(8, 15, 22, 24);  // palm
        Rect(11, 24, 19, 28); // wrist
        // Soften a few blocky corners so the silhouette reads as a hand.
        Rect(7, 16, 8, 20);
        Rect(22, 16, 23, 19);
    }

    static bool NearFill(bool[,] fill, int size, int x, int y, int radius)
    {
        var x0 = Math.Max(0, x - radius);
        var y0 = Math.Max(0, y - radius);
        var x1 = Math.Min(size - 1, x + radius);
        var y1 = Math.Min(size - 1, y + radius);
        for (var yy = y0; yy <= y1; yy++)
            for (var xx = x0; xx <= x1; xx++)
                if (fill[xx, yy]) return true;
        return false;
    }
}
