using System.Net;
using System.Net.Mail;
using Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Infrastructure.Authentication;

public sealed class TransactionalEmailSender(IOptions<SmtpEmailOptions> options) : ITransactionalEmailSender
{
    public async Task SendNotificationAsync(string recipient, string title, string text, string url, CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.FromAddress))
            throw new InvalidOperationException("Email delivery is not configured.");
        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, "TREBA"), Subject = title,
            Body = $"<html lang=\"uk\"><body><h1>{WebUtility.HtmlEncode(title)}</h1><p>{WebUtility.HtmlEncode(text)}</p><p><a href=\"{WebUtility.HtmlEncode(url)}\">Відкрити TREBA</a></p></body></html>",
            IsBodyHtml = true
        };
        message.To.Add(new MailAddress(recipient));
        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.UseSsl, DeliveryMethod = SmtpDeliveryMethod.Network, UseDefaultCredentials = false,
            Credentials = string.IsNullOrWhiteSpace(settings.Username) ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(settings.Username, settings.Password)
        };
        await client.SendMailAsync(message, ct);
    }
}
