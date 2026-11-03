using Microsoft.Extensions.Options;

namespace Kofre.Server.Email;

/// <summary>Emails to the admin, when <see cref="EmailOptions.Enabled"/>. A failed email is logged, never thrown.</summary>
internal sealed class AdminMail(IOptions<KofreOptions> options, IMailSender sender, ILogger<AdminMail> log)
{
    public bool Enabled => options.Value.Email.Enabled;

    public async Task SendAsync(string subject, string body, CancellationToken cancellation = default)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            await sender.SendAsync($"Kofre server {Environment.MachineName}: {subject}", body, cancellation);
            log.LogInformation("Email to the admin sent: {Subject}", subject);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // SMTP fails in many ways (network, TLS, sign-in, address): none may stop the server.
            log.LogWarning(exception, "Email to the admin failed: {Subject}", subject);
        }
    }

    /// <summary>Sends in the background, so a request does not wait for the SMTP server.</summary>
    public void Post(string subject, string body) => _ = SendAsync(subject, body);
}
