using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;
using Size = SixLabors.ImageSharp.Size;

namespace ImmichUploaderApp.Services;

/// Small activity-panel preview thumbnails, shared between UploadWatcherService (originals) and
/// PhotoSyncService (originals or webp previews depending on sync mode). Uses ImageSharp rather
/// than System.Drawing/GDI+ because GDI+ has no WebP codec at all - Image.FromFile on a webp
/// file throws a bare OutOfMemoryException (GDI+'s catch-all "unsupported format" error),
/// silently killing every Thumbnail-mode preview (verified against a real downloaded file).
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
        catch { return null; }
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
}
