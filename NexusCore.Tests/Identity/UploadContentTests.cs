using System.Text;
using NexusCore.Application.Files;

namespace NexusCore.Tests.Identity;

/// <summary>A file is accepted by its extension and its first bytes - never by the content-type a client sends.</summary>
public sealed class UploadContentTests
{
    private static byte[] Bytes(string text) => Encoding.ASCII.GetBytes(text);

    [Fact]
    public void Html_or_svg_renamed_to_an_allowed_extension_is_rejected()
    {
        var html = Bytes("<html><script>alert(1)</script></html>");
        Assert.False(AllowedUploadTypes.HeadMatches("x.pdf", html));
        Assert.False(AllowedUploadTypes.HeadMatches("x.png", Bytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script/></svg>")));
        Assert.False(AllowedUploadTypes.HeadMatches("x.docx", html));
    }

    [Fact]
    public void Genuine_files_match_their_extension()
    {
        Assert.True(AllowedUploadTypes.HeadMatches("a.pdf", Bytes("%PDF-1.7\n")));
        Assert.True(AllowedUploadTypes.HeadMatches("a.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));
        Assert.True(AllowedUploadTypes.HeadMatches("a.JPG", [0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.True(AllowedUploadTypes.HeadMatches("a.xlsx", [0x50, 0x4B, 0x03, 0x04, 0x14]));
        Assert.True(AllowedUploadTypes.HeadMatches("a.doc", [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]));
        Assert.True(AllowedUploadTypes.HeadMatches("voice-1.webm", [0x1A, 0x45, 0xDF, 0xA3]));
        Assert.True(AllowedUploadTypes.HeadMatches("a.webp", Bytes("RIFF....WEBP")));
    }

    [Fact]
    public void An_unknown_extension_or_empty_content_never_matches()
    {
        Assert.False(AllowedUploadTypes.HeadMatches("a.html", Bytes("<html>")));
        Assert.False(AllowedUploadTypes.HeadMatches("a.pdf", []));
        Assert.False(AllowedUploadTypes.HeadMatches(null, Bytes("%PDF")));
    }

    [Fact]
    public void The_client_content_type_grants_nothing()
    {
        Assert.False(AllowedUploadTypes.IsAllowed("x.html", "image/png"));
        Assert.False(AllowedUploadTypes.IsAllowed("x.svg", "image/svg+xml"));
        Assert.False(AllowedUploadTypes.IsVoiceMessage("x.html", "audio/webm"));
        Assert.True(AllowedUploadTypes.IsAllowed("x.pdf", "text/html"));   // the bytes are checked next
    }

    [Fact]
    public void The_served_type_comes_from_the_extension()
    {
        Assert.Equal("application/pdf", AllowedUploadTypes.ContentTypeFor("report.PDF"));
        Assert.Equal("audio/webm", AllowedUploadTypes.ContentTypeFor("voice-1.webm"));
        Assert.Equal("application/octet-stream", AllowedUploadTypes.ContentTypeFor("evil.html"));
    }

    [Fact]
    public async Task A_stream_is_checked_and_put_back()
    {
        await using var good = new MemoryStream(Bytes("%PDF-1.4 rest"));
        Assert.True(await AllowedUploadTypes.StreamMatchesAsync("a.pdf", good, CancellationToken.None));
        Assert.Equal(0, good.Position);

        await using var bad = new MemoryStream(Bytes("<html>"));
        Assert.False(await AllowedUploadTypes.StreamMatchesAsync("a.pdf", bad, CancellationToken.None));
    }
}
