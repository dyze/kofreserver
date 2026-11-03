using System.Threading.Channels;
using Kofre.Server.Email;

namespace Kofre.Server.Tests;

internal sealed record SentMail(string Subject, string Body);

/// <summary>Keeps the emails instead of sending them; fails with <see cref="Failure"/> when set.</summary>
internal sealed class FakeMailSender : IMailSender
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly Channel<SentMail> _sent = Channel.CreateUnbounded<SentMail>();

    public Exception? Failure { get; set; }

    public int Waiting => _sent.Reader.Count;

    public Task SendAsync(string subject, string body, CancellationToken cancellation)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        _sent.Writer.TryWrite(new SentMail(subject, body));
        return Task.CompletedTask;
    }

    /// <summary>The next email whose subject contains <paramref name="subject"/>, the others before it skipped.</summary>
    public async Task<SentMail> Next(string subject)
    {
        while (true)
        {
            var mail = await _sent.Reader.ReadAsync().AsTask().WaitAsync(Wait);
            if (mail.Subject.Contains(subject, StringComparison.Ordinal))
            {
                return mail;
            }
        }
    }
}
