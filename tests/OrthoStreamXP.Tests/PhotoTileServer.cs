using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using OrthoStreamXP.Textures;
using SkiaSharp;

namespace OrthoStreamXP.Tests;

/// <summary>
/// An XYZ tile server ("http://t{s}.test/{z}/{x}/{y}") answering a JPEG photo that differs per tile (the synthetic
/// pattern of that tile), so builds go through real JPEG decoding. <paramref name="missing"/> answers 404 (no provider
/// has it); <paramref name="large"/> answers that tile at 512 x 512, as providers occasionally do.
/// </summary>
internal sealed class PhotoTileServer(Func<int, int, int, bool>? missing = null, Func<int, int, int, bool>? large = null) : HttpMessageHandler
{
    public const string Template = "http://t{s}.test/{z}/{x}/{y}";

    private readonly ConcurrentDictionary<(int, int, int, bool), byte[]> _encoded = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string[] parts = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
        int zoom = int.Parse(parts[0], CultureInfo.InvariantCulture), x = int.Parse(parts[1], CultureInfo.InvariantCulture),
            y = int.Parse(parts[2], CultureInfo.InvariantCulture);
        if (missing?.Invoke(x, y, zoom) == true) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new ByteArrayContent([]) });
        bool big = large?.Invoke(x, y, zoom) == true;
        byte[] body = _encoded.GetOrAdd((x, y, zoom, big), key => Jpeg(key.Item1, key.Item2, key.Item3, key.Item4));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    /// <summary>The tile's synthetic pattern as a JPEG, 256 px or (<paramref name="big"/>) stretched to 512 px.</summary>
    public static byte[] Jpeg(int x, int y, int zoom, bool big = false)
    {
        int size = big ? 512 : Tiles.TilePx;
        byte[] rgb = Images.SyntheticTile(x, y, zoom);
        if (big) rgb = Images.ResizeBicubic(rgb, Tiles.TilePx, Tiles.TilePx, size, size, 1);
        using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Opaque));
        Span<byte> pixels = bitmap.GetPixelSpan();
        for (int i = 0, o = 0; i < rgb.Length; i += 3, o += 4)
        {
            pixels[o] = rgb[i];
            pixels[o + 1] = rgb[i + 1];
            pixels[o + 2] = rgb[i + 2];
            pixels[o + 3] = 255;
        }
        using SKData jpeg = bitmap.Encode(SKEncodedImageFormat.Jpeg, 85);
        return jpeg.ToArray();
    }
}
