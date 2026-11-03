namespace Kofre.Server.Email;

/// <summary>The <c>Kofre:Email</c> section: emails to the admin, through an SMTP server.</summary>
public sealed class EmailOptions
{
    public bool Enabled { get; set; }

    public string Host { get; set; } = "";

    public int Port { get; set; } = 587;

    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;

    /// <summary>Empty: no sign-in to the SMTP server.</summary>
    public string User { get; set; } = "";

    public string Password { get; set; } = "";

    public string From { get; set; } = "";

    /// <summary>The admin's address.</summary>
    public string To { get; set; } = "";

    /// <summary>The server's local time of the daily summary.</summary>
    public TimeSpan SummaryTime { get; set; } = new(7, 0, 0);
}

public enum SmtpSecurity
{
    StartTls,
    SslOnConnect,

    /// <summary>No encryption: only for an SMTP server on this machine.</summary>
    None,
}
