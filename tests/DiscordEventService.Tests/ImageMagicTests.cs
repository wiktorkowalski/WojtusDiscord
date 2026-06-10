using DiscordEventService.Services.MemeIndexing;
using Xunit;

namespace DiscordEventService.Tests;

public sealed class ImageMagicTests
{
    [Theory]
    [InlineData("meme.jpg", true)]
    [InlineData("MEME.PNG", true)]
    [InlineData("anim.webp", true)]
    [InlineData("anim.gif", true)]
    [InlineData("photo.jpeg", true)]
    [InlineData("clip.mp4", false)]
    [InlineData("clip.mov", false)]
    [InlineData("notes.txt", false)]
    [InlineData("noextension", false)]
    public void IsIndexableFileName_FiltersByExtension(string fileName, bool expected) =>
        Assert.Equal(expected, ImageMagic.IsIndexableFileName(fileName));

    [Fact]
    public void SniffMimeType_RecognizesJpeg() =>
        Assert.Equal("image/jpeg", ImageMagic.SniffMimeType(Pad([0xFF, 0xD8, 0xFF, 0xE0])));

    [Fact]
    public void SniffMimeType_RecognizesPng() =>
        Assert.Equal("image/png", ImageMagic.SniffMimeType(Pad([0x89, 0x50, 0x4E, 0x47])));

    [Fact]
    public void SniffMimeType_RecognizesGif() =>
        Assert.Equal("image/gif", ImageMagic.SniffMimeType(Pad("GIF89a"u8.ToArray())));

    [Fact]
    public void SniffMimeType_RecognizesWebp()
    {
        var bytes = Pad("RIFF\0\0\0\0WEBP"u8.ToArray());
        Assert.Equal("image/webp", ImageMagic.SniffMimeType(bytes));
    }

    [Fact]
    public void SniffMimeType_RejectsRiffThatIsNotWebp()
    {
        // The POC's bug: a bare RIFF prefix also matches wav/avi containers.
        var bytes = Pad("RIFF\0\0\0\0WAVE"u8.ToArray());
        Assert.Null(ImageMagic.SniffMimeType(bytes));
    }

    [Fact]
    public void SniffMimeType_RejectsUnknownAndShortInput()
    {
        Assert.Null(ImageMagic.SniffMimeType(Pad([0x00, 0x01, 0x02, 0x03])));
        Assert.Null(ImageMagic.SniffMimeType([0xFF, 0xD8]));
    }

    private static byte[] Pad(byte[] prefix)
    {
        var bytes = new byte[16];
        prefix.CopyTo(bytes, 0);
        return bytes;
    }
}
