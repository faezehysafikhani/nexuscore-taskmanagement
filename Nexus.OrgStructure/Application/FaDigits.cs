namespace Nexus.OrgStructure.Application;

/// <summary>Numbers in the messages people read: Persian digits («۱۶۰»), as the rest of the screens show them.</summary>
public static class FaDigits
{
    public static string Of(long? value) => value is null ? string.Empty : Convert(value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static string Convert(string text) =>
        string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = source[i] is >= '0' and <= '9' ? (char)('۰' + (source[i] - '0')) : source[i];
            }
        });
}
