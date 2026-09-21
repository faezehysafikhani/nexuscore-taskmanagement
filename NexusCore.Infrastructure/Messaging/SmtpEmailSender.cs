using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusCore.Application.Messaging;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Infrastructure.Messaging;

/// <summary>SMTP email (System.Net.Mail), or .eml files in a pickup directory for development.</summary>
public sealed class SmtpEmailSender(IOptions<SmtpEmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpEmailOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            return Result.Failure(Error.Validation("Email delivery is not configured (Email:Smtp)."));
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(_options.FromAddress!, _options.FromName),
            Subject = message.Subject,
            Body = message.Body,
            IsBodyHtml = message.IsHtml,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8
        };
        mail.To.Add(message.To);

        using var client = new SmtpClient();
        if (!string.IsNullOrWhiteSpace(_options.PickupDirectory))
        {
            var directory = Path.GetFullPath(_options.PickupDirectory);
            Directory.CreateDirectory(directory);
            client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
            client.PickupDirectoryLocation = directory;
        }
        else
        {
            client.Host = _options.Host!;
            client.Port = _options.Port;
            client.EnableSsl = _options.EnableSsl;
            if (!string.IsNullOrWhiteSpace(_options.UserName))
            {
                client.Credentials = new NetworkCredential(_options.UserName, _options.Password);
            }
        }

        try
        {
            await client.SendMailAsync(mail, cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or IOException)
        {
            logger.LogWarning(ex, "Email to {Recipient} could not be sent.", MaskAddress(message.To));
            return Result.Failure(Error.Validation("The email could not be sent."));
        }
    }

    private static string MaskAddress(string address)
    {
        var at = address.IndexOf('@');
        return at <= 1 ? "***" : $"{address[0]}***{address[at..]}";
    }
}
