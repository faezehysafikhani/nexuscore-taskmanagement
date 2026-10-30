namespace Nexus.TaskManagement.Tests;

/// <summary>
/// Files the upload rules accept: only Excel, Word, PDF, image and voice files whose content starts
/// as their type does. A small PDF: its «%PDF-» head, then a pattern up to the length asked.
/// </summary>
internal static class TestFiles
{
    public static byte[] Pdf(int length)
    {
        var bytes = Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
        "%PDF-1.4\n"u8.ToArray().AsSpan(0, Math.Min(9, length)).CopyTo(bytes);
        return bytes;
    }
}
