using ImageMagick;

namespace WebApi.Media;

public static class SafeImage
{
    static SafeImage()
    {
        ResourceLimits.Memory = 128 * 1024 * 1024;
        ResourceLimits.Disk = 0;
        ResourceLimits.Width = 16000;
        ResourceLimits.Height = 16000;
        ResourceLimits.ListLength = 200;
        ResourceLimits.Thread = 2;
    }

    public static byte[] Normalize(byte[] bytes, string extension)
    {
        var format = extension.TrimStart('.').ToLowerInvariant() switch
        {
            "png" => MagickFormat.Png,
            "jpg" or "jpeg" => MagickFormat.Jpeg,
            "webp" => MagickFormat.WebP,
            "gif" => MagickFormat.Gif,
            _ => throw new ArgumentException("Unsupported image format.")
        };
        try
        {
            var input = bytes.AsSpan();
            var inputFormat = input.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47 }) ? MagickFormat.Png
                : input.StartsWith(new byte[] { 0xff, 0xd8, 0xff }) ? MagickFormat.Jpeg
                : input.StartsWith("GIF8"u8) ? MagickFormat.Gif
                : input.Length > 12 && input[..4].SequenceEqual("RIFF"u8) && input.Slice(8, 4).SequenceEqual("WEBP"u8) ? MagickFormat.WebP
                : throw new ArgumentException("Unsupported image format.");
            var settings = new MagickReadSettings { Format = inputFormat };
            using var images = new MagickImageCollection();
            images.Ping(bytes, settings);
            if (images.Count > 200 || images.Sum(i => (long)i.Width * i.Height) > 16_000_000)
                throw new ArgumentException("The image has too many pixels or frames.");
            images.Read(bytes, settings);
            foreach (var image in images)
            {
                image.AutoOrient();
                image.Strip();
            }
            var result = images.ToByteArray(format);
            if (result.Length > 5 * 1024 * 1024)
                throw new ArgumentException("The normalized image exceeds 5 MB.");
            return result;
        }
        catch (MagickException ex)
        {
            throw new ArgumentException("Invalid image content.", ex);
        }
    }
}
