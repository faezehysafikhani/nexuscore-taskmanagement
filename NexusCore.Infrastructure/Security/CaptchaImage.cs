using System.IO.Compression;
using System.Security.Cryptography;

namespace NexusCore.Infrastructure.Security;

/// <summary>
/// Draws a CAPTCHA code as a PNG: every digit is a set of strokes, individually scaled,
/// rotated, shifted and bent, over and under random noise lines and dots. The result is only
/// pixels - the code appears nowhere as text - and it is built with the framework alone (no
/// System.Drawing, which is Windows-only, and no image library).
/// </summary>
internal static class CaptchaImage
{
    private const int Width = 200;
    private const int Height = 70;

    // Digit shapes as polylines in a 0.6 x 1.0 box (y grows downwards).
    private static readonly Dictionary<char, (double X, double Y)[][]> Glyphs = new()
    {
        ['0'] = [Ellipse(0.3, 0.5, 0.26, 0.47, 18)],
        ['1'] = [[(0.12, 0.22), (0.34, 0.03), (0.34, 1.0)], [(0.14, 1.0), (0.52, 1.0)]],
        ['2'] = [[(0.04, 0.22), (0.14, 0.05), (0.32, 0.0), (0.5, 0.06), (0.57, 0.24), (0.5, 0.42), (0.04, 1.0), (0.6, 1.0)]],
        ['3'] = [[(0.04, 0.1), (0.2, 0.01), (0.44, 0.03), (0.56, 0.2), (0.48, 0.4), (0.24, 0.47)],
                 [(0.46, 0.5), (0.59, 0.7), (0.52, 0.92), (0.28, 1.0), (0.04, 0.9)]],
        ['4'] = [[(0.46, 1.0), (0.46, 0.02), (0.0, 0.7), (0.6, 0.7)]],
        ['5'] = [[(0.56, 0.02), (0.1, 0.02), (0.05, 0.45), (0.3, 0.38), (0.52, 0.47), (0.59, 0.7), (0.5, 0.93), (0.26, 1.0), (0.03, 0.9)]],
        ['6'] = [[(0.52, 0.04), (0.26, 0.14), (0.08, 0.44), (0.05, 0.76), (0.2, 0.98), (0.42, 0.98), (0.57, 0.78), (0.5, 0.57), (0.3, 0.51), (0.08, 0.62)]],
        ['7'] = [[(0.0, 0.03), (0.6, 0.03), (0.22, 1.0)], [(0.15, 0.5), (0.48, 0.5)]],
        ['8'] = [Ellipse(0.3, 0.25, 0.21, 0.23, 14), Ellipse(0.3, 0.73, 0.27, 0.26, 16)],
        ['9'] = [[(0.54, 0.4), (0.32, 0.5), (0.1, 0.42), (0.05, 0.2), (0.2, 0.02), (0.42, 0.02), (0.56, 0.2), (0.53, 0.56), (0.36, 0.9), (0.1, 0.99)]],
    };

    public static byte[] Render(string code)
    {
        var pixels = new byte[Width * Height * 3];
        var background = (R: Next(235, 252), G: Next(235, 252), B: Next(235, 252));
        for (var i = 0; i < pixels.Length; i += 3)
        {
            pixels[i] = (byte)background.R;
            pixels[i + 1] = (byte)background.G;
            pixels[i + 2] = (byte)background.B;
        }

        // Noise under the digits.
        for (var i = 0; i < 350; i++)
        {
            Dot(pixels, Next(0, Width), Next(0, Height), RandomColor(120, 210));
        }

        for (var i = 0; i < 3; i++)
        {
            DrawPolyline(pixels, NoiseCurve(), Next(10, 22) / 10.0, RandomColor(110, 190));
        }

        // The digits.
        var slot = (Width - 20.0) / code.Length;
        for (var i = 0; i < code.Length; i++)
        {
            var size = Next(36, 46);
            var angle = (Next(-26, 27)) * Math.PI / 180;
            var originX = 12 + i * slot + Next(0, (int)Math.Max(1, slot - size * 0.6));
            var originY = (Height - size) / 2.0 + Next(-6, 7);
            var shear = Next(-20, 21) / 100.0;
            var stroke = Next(28, 45) / 10.0;
            var color = RandomColor(20, 110);

            foreach (var line in Glyphs[code[i]])
            {
                var points = line.Select(p =>
                {
                    // Bend each point a little, shear, rotate around the glyph centre, place.
                    var x = p.X + Next(-40, 41) / 1000.0 + shear * (p.Y - 0.5);
                    var y = p.Y + Next(-40, 41) / 1000.0;
                    var cx = (x - 0.3) * size;
                    var cy = (y - 0.5) * size;
                    return (X: originX + size * 0.3 + cx * Math.Cos(angle) - cy * Math.Sin(angle),
                            Y: originY + size * 0.5 + cx * Math.Sin(angle) + cy * Math.Cos(angle));
                }).ToArray();
                DrawPolyline(pixels, points, stroke, color);
            }
        }

        // Noise over the digits: lines that cross them, so they cannot simply be separated.
        for (var i = 0; i < 2; i++)
        {
            DrawPolyline(pixels, NoiseCurve(), Next(12, 22) / 10.0, RandomColor(40, 140));
        }

        for (var i = 0; i < 120; i++)
        {
            Dot(pixels, Next(0, Width), Next(0, Height), RandomColor(60, 200));
        }

        return EncodePng(pixels);
    }

    private static (double X, double Y)[] Ellipse(double cx, double cy, double rx, double ry, int steps) =>
        Enumerable.Range(0, steps + 1)
            .Select(i => (cx + rx * Math.Cos(2 * Math.PI * i / steps), cy + ry * Math.Sin(2 * Math.PI * i / steps)))
            .ToArray();

    private static (double X, double Y)[] NoiseCurve()
    {
        double y0 = Next(5, Height - 5), y1 = Next(5, Height - 5), amplitude = Next(4, 16), phase = Next(0, 628) / 100.0;
        var frequency = Next(8, 30) / 1000.0;
        return Enumerable.Range(0, 41)
            .Select(i =>
            {
                var x = i * Width / 40.0;
                return (x, y0 + (y1 - y0) * i / 40.0 + amplitude * Math.Sin(frequency * x * 6 + phase));
            })
            .ToArray();
    }

    private static void DrawPolyline(byte[] pixels, (double X, double Y)[] points, double width, (int R, int G, int B) color)
    {
        for (var i = 1; i < points.Length; i++)
        {
            DrawSegment(pixels, points[i - 1], points[i], width, color);
        }
    }

    /// <summary>A thick segment with round ends, anti-aliased by distance to the segment.</summary>
    private static void DrawSegment(byte[] pixels, (double X, double Y) a, (double X, double Y) b, double width, (int R, int G, int B) color)
    {
        var half = width / 2;
        var minX = (int)Math.Max(0, Math.Floor(Math.Min(a.X, b.X) - half - 1));
        var maxX = (int)Math.Min(Width - 1, Math.Ceiling(Math.Max(a.X, b.X) + half + 1));
        var minY = (int)Math.Max(0, Math.Floor(Math.Min(a.Y, b.Y) - half - 1));
        var maxY = (int)Math.Min(Height - 1, Math.Ceiling(Math.Max(a.Y, b.Y) + half + 1));
        double dx = b.X - a.X, dy = b.Y - a.Y, lengthSquared = dx * dx + dy * dy;

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                double px = x + 0.5, py = y + 0.5;
                var t = lengthSquared == 0 ? 0 : Math.Clamp(((px - a.X) * dx + (py - a.Y) * dy) / lengthSquared, 0, 1);
                double ex = px - (a.X + t * dx), ey = py - (a.Y + t * dy);
                var coverage = Math.Clamp(half + 0.5 - Math.Sqrt(ex * ex + ey * ey), 0, 1);
                if (coverage > 0)
                {
                    Blend(pixels, x, y, color, coverage);
                }
            }
        }
    }

    private static void Dot(byte[] pixels, int x, int y, (int R, int G, int B) color) => Blend(pixels, x, y, color, 1);

    private static void Blend(byte[] pixels, int x, int y, (int R, int G, int B) color, double alpha)
    {
        var i = (y * Width + x) * 3;
        pixels[i] = (byte)(pixels[i] + (color.R - pixels[i]) * alpha);
        pixels[i + 1] = (byte)(pixels[i + 1] + (color.G - pixels[i + 1]) * alpha);
        pixels[i + 2] = (byte)(pixels[i + 2] + (color.B - pixels[i + 2]) * alpha);
    }

    private static (int R, int G, int B) RandomColor(int min, int max) => (Next(min, max), Next(min, max), Next(min, max));

    private static int Next(int minInclusive, int maxExclusive) => RandomNumberGenerator.GetInt32(minInclusive, maxExclusive);

    // ---- PNG (8-bit RGB, no filtering, zlib-compressed) ----

    private static byte[] EncodePng(byte[] rgb)
    {
        var raw = new byte[(Width * 3 + 1) * Height];
        for (var y = 0; y < Height; y++)
        {
            raw[y * (Width * 3 + 1)] = 0; // filter type: none
            Buffer.BlockCopy(rgb, y * Width * 3, raw, y * (Width * 3 + 1) + 1, Width * 3);
        }

        byte[] compressed;
        using (var buffer = new MemoryStream())
        {
            using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(raw);
            }

            compressed = buffer.ToArray();
        }

        using var png = new MemoryStream();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        WriteBigEndian(header, 0, Width);
        WriteBigEndian(header, 4, Height);
        header[8] = 8;  // bit depth
        header[9] = 2;  // colour type: truecolour
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed);
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        stream.Write(length);

        var typeAndData = new byte[4 + data.Length];
        for (var i = 0; i < 4; i++)
        {
            typeAndData[i] = (byte)type[i];
        }

        Buffer.BlockCopy(data, 0, typeAndData, 4, data.Length);
        stream.Write(typeAndData);

        var crc = new byte[4];
        WriteBigEndian(crc, 0, (int)Crc32(typeAndData));
        stream.Write(crc);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++)
        {
            c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        }

        return c;
    }).ToArray();

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
