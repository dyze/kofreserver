namespace Kofre.Server.Email;

/// <summary>Sends a plain-text email to the admin.</summary>
internal interface IMailSender
{
    Task SendAsync(string subject, string body, CancellationToken cancellation);
}
