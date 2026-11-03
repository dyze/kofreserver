using Kofre.Server.Storage;

namespace Kofre.Server.Tests;

/// <summary>Every disk with the space the test sets, or failing with <see cref="Failure"/>.</summary>
internal sealed class FakeDiskSpace : IDiskSpace
{
    public DiskUsage Usage { get; set; } = new(50, 100);

    public Exception? Failure { get; set; }

    public DiskUsage Of(string folder) => Failure is null ? Usage : throw Failure;
}
