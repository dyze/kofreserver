using System.Threading.Channels;
using Kofre.Server.Email;

namespace Kofre.Server.Tests;

internal sealed record SentMail(string Subject, string Body);

/// <summary>Keeps the emails instead of sending them; fails with <see cref="Failure"/> when set.</summary>
internal sealed class FakeMailSender : IMailSender
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly Channel<SentMail> _sent = Channel.CreateUnbounded<SentMail>();
    private readonly System.Collections.Concurrent.ConcurrentQueue<SentMail> _all = new();

    public Exception? Failure { get; set; }

    public int Waiting => _sent.Reader.Count;

    /// <summary>The subjects of all the emails sent, read by <see cref="Next"/> or not.</summary>
    public IReadOnlyList<string> Subjects => Sent.Select(mail => mail.Subject).ToList();

    public IReadOnlyList<SentMail> Sent => [.. _all];

    public int Count(string subject) => Subjects.Count(sent => sent.Contains(subject, StringComparison.Ordinal));

    public Task SendAsync(string subject, string body, CancellationToken cancellation)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        _all.Enqueue(new SentMail(subject, body));
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
