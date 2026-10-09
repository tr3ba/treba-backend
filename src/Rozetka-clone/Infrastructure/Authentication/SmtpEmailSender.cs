using System.Net;
using System.Net.Mail;
using Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Infrastructure.Authentication;

public sealed class SmtpEmailSender(
    IOptions<SmtpEmailOptions> options
) : IEmailSender
{
    public async Task SendSecurityCodeAsync(
        string recipient,
        string displayName,
        string code,
        TimeSpan validFor,
        CancellationToken cancellationToken = default
    )
    {
        var settings = options.Value;
        if (
            !settings.Enabled
            || string.IsNullOrWhiteSpace(settings.Host)
            || string.IsNullOrWhiteSpace(settings.FromAddress)
        )
        {
            throw new InvalidOperationException(
                "Email delivery is not configured. Fill the Email:Smtp section in appsettings."
            );
        }

        using var message = new MailMessage
        {
            From = new MailAddress(
                settings.FromAddress,
                settings.FromName
            ),
            Subject = "TREBA — security code",
            Body = BuildBody(
                displayName,
                code,
                validFor
            ),
            IsBodyHtml = true,
        };
        message.To.Add(new MailAddress(recipient));

        using var client = new SmtpClient(
            settings.Host,
            settings.Port
        )
        {
            EnableSsl = settings.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = string.IsNullOrWhiteSpace(settings.Username)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(
                    settings.Username,
                    settings.Password
                ),
        };

        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(
            message,
            cancellationToken
        );
    }

    private static string BuildBody(
        string displayName,
        string code,
        TimeSpan validFor
    )
    {
        var safeName = WebUtility.HtmlEncode(displayName);
        var safeCode = WebUtility.HtmlEncode(code);
        var minutes = Math
            .Ceiling(validFor.TotalMinutes)
            .ToString(
                "0",
                System.Globalization.CultureInfo.InvariantCulture
            );
        return $$"""
            <!doctype html>
            <html lang="en">
            <body style="margin:0;background:#f4f6fa;font-family:Arial,sans-serif;color:#111827">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="padding:32px 12px;background:#f4f6fa">
                <tr><td align="center">
                  <table role="presentation" width="520" cellspacing="0" cellpadding="0" style="max-width:520px;background:#fff;border:1px solid #e5e7eb;border-radius:18px;overflow:hidden">
                    <tr><td style="height:5px;background:#f97316"></td></tr>
                    <tr><td style="padding:30px 34px">
                      <div style="font-size:20px;font-weight:800;letter-spacing:1px">TREBA</div>
                      <p style="margin:28px 0 8px;font-size:20px;font-weight:700">Hello, {{safeName}}</p>
                      <p style="margin:0 0 22px;color:#64748b;font-size:14px;line-height:1.6">Use this code to confirm your sign-in or enable email protection.</p>
                      <div style="padding:18px;border-radius:13px;background:#fff7ed;color:#c2410c;text-align:center;font-size:32px;font-weight:800;letter-spacing:8px">{{safeCode}}</div>
                      <p style="margin:22px 0 0;color:#64748b;font-size:12px;line-height:1.6">The code is valid for {{minutes}} minutes. If you did not request it, you can safely ignore this email.</p>
                    </td></tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }
}
