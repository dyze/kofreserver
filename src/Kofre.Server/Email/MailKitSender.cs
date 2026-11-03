using System.Diagnostics.CodeAnalysis;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Kofre.Server.Email;

/// <summary>Sends through the SMTP server of <see cref="EmailOptions"/>, one connection per email.</summary>
[ExcludeFromCodeCoverage(Justification = "Needs a real SMTP server; the tests replace it.")]
internal sealed class MailKitSender(IOptions<KofreOptions> options) : IMailSender
{
    private const int TimeoutMilliseconds = 30_000;

    public async Task SendAsync(string subject, string body, CancellationToken cancellation)
    {
        var settings = options.Value.Email;
        using var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(settings.From));
        message.To.Add(MailboxAddress.Parse(settings.To));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var smtp = new SmtpClient { Timeout = TimeoutMilliseconds };
        await smtp.ConnectAsync(settings.Host, settings.Port, SocketOptions(settings.Security), cancellation);
        if (settings.User.Length > 0)
        {
            await smtp.AuthenticateAsync(settings.User, settings.Password, cancellation);
        }

        await smtp.SendAsync(message, cancellation);
        await smtp.DisconnectAsync(quit: true, cancellation);
    }

    private static SecureSocketOptions SocketOptions(SmtpSecurity security) => security switch
    {
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        SmtpSecurity.None => SecureSocketOptions.None,
        _ => SecureSocketOptions.StartTls,
    };
}
