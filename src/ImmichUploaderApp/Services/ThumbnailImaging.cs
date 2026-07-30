using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Image = SixLabors.ImageSharp.Image;
using Size = SixLabors.ImageSharp.Size;

namespace ImmichUploaderApp.Services;

/// Small activity-panel preview thumbnails, shared between UploadWatcherService (originals) and
/// PhotoSyncService (originals or webp previews depending on sync mode). Uses ImageSharp rather
/// than System.Drawing/GDI+ because GDI+ has no WebP codec at all - Image.FromFile on a webp
/// file throws a bare OutOfMemoryException (GDI+'s catch-all "unsupported format" error),
/// silently killing every Thumbnail-mode preview (verified against a real downloaded file).
/// ImageSharp itself has no HEIC or RAW decoder either (common for Original-mode iPhone photos),
/// so those fall back to WIC via WPF's imaging APIs - the OS's own codecs, used by Windows Photos,
/// which decode HEIC/RAW when the user has the (free, Store-installed) HEIF/RAW extensions.
public static class ThumbnailImaging
{
    private const int ThumbnailSize = 96;

    public static byte[]? TryCreate(string filePath)
    {
        try
        {
            using var original = Image.Load(filePath);
            return Resize(original);
        }
        catch { return TryCreateViaWic(filePath); }
    }

    public static byte[]? TryCreate(byte[] bytes)
    {
        try
        {
            using var original = Image.Load(bytes);
            return Resize(original);
        }
        catch { return null; }
    }

    private static byte[] Resize(Image image)
    {
        image.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(ThumbnailSize, ThumbnailSize),
            Mode = ResizeMode.Max,
        }));
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    private static byte[]? TryCreateViaWic(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            var scale = Math.Min((double)ThumbnailSize / frame.PixelWidth, (double)ThumbnailSize / frame.PixelHeight);
            var resized = new TransformedBitmap(frame, new ScaleTransform(scale, scale));

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(resized));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }
}
