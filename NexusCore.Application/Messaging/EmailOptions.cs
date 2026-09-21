namespace NexusCore.Application.Messaging;

/// <summary>
/// Email:Smtp. Supply the password through user-secrets or environment variables, never
/// appsettings.json. PickupDirectory, when set, writes .eml files instead of sending - for
/// development and tests.
/// </summary>
public sealed class SmtpEmailOptions
{
    public const string SectionName = "Email:Smtp";

    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }
    public string? PickupDirectory { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(FromAddress)
        && (!string.IsNullOrWhiteSpace(Host) || !string.IsNullOrWhiteSpace(PickupDirectory));
}
