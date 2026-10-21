namespace Kofre.Server.Stores;

/// <summary>
/// The paths of files in a store: segments of letters, digits, <c>.</c>, <c>_</c> and <c>-</c>,
/// separated by <c>/</c>, so no path can leave the store's folder.
/// </summary>
internal static class StorePath
{
    private const int MaxLength = 512;
    private const int MaxSegments = 16;
    private const int MaxSegmentLength = 128;

    public static bool IsValid(string? path) =>
        path is { Length: > 0 and <= MaxLength }
        && path.Split('/') is { Length: <= MaxSegments } segments
        && segments.All(IsValidSegment);

    private static bool IsValidSegment(string segment) =>
        segment is { Length: > 0 and <= MaxSegmentLength } and not "." and not ".."
        && segment.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}
