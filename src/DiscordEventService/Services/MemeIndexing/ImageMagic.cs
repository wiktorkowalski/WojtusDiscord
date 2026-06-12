namespace DiscordEventService.Services.MemeIndexing;

public static class ImageMagic
{
    private static readonly string[] ImageExtensions = ["jpg", "jpeg", "png", "webp", "gif"];

    public static bool IsIndexableFileName(string fileName)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return ImageExtensions.Contains(ext);
    }

    // Filenames lie (FB_IMG_*.jpg that is really png) — bytes decide what we tell the vision model.
    public static string? SniffMimeType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12)
            return null;

        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";

        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "image/png";

        if (bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == '8')
            return "image/gif";

        // RIFF container is not enough (wav/avi share it) — bytes 8-11 must say WEBP.
        if (bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F'
            && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P')
            return "image/webp";

        return null;
    }
}
