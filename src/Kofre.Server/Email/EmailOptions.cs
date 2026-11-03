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

    /// <summary>Whether a successful backup is emailed too, not only a failed one.</summary>
    public bool BackupDone { get; set; } = true;

    /// <summary>Below this share of free space on the data or backups disk, the admin is told once, then once it is back above.</summary>
    public int MinFreeDiskPercent { get; set; } = 20;

    /// <summary>Emails sent at most per hour, so a fault cannot flood the admin's mailbox; the others are only logged.</summary>
    public int MaxPerHour { get; set; } = 10;

    /// <summary>Refused sign-ins in an hour from which the admin is told: someone may be trying passwords.</summary>
    public int MaxRefusedSignInsPerHour { get; set; } = 50;

    public const string Rule = "Kofre:Email: Host, From and To are required when Enabled is true; From and To must be email addresses";

    /// <summary>Off, or enough to send: checked at start, not at the first email.</summary>
    public bool IsValid => !Enabled || (Host.Length > 0 && IsAddress(From) && IsAddress(To));

    // MimeKit also accepts a name without a domain: one is required here.
    private static bool IsAddress(string text) => MimeKit.MailboxAddress.TryParse(text, out var address) && address.Address.Contains('@');
}

public enum SmtpSecurity
{
    StartTls,
    SslOnConnect,

    /// <summary>No encryption: only for an SMTP server on this machine.</summary>
    None,
}
