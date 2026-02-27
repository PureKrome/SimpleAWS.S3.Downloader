namespace SimpleAWS.S3.Downloader.Core.Models;

/// <summary>
/// Summary information about the objects in an S3 bucket.
/// </summary>
public sealed record BucketSummary
{
    /// <summary>
    /// Gets the total number of downloadable objects (excluding folder markers).
    /// </summary>
    public int ObjectCount { get; init; }

    /// <summary>
    /// Gets the total size of all objects in bytes.
    /// </summary>
    public long TotalSizeBytes { get; init; }
}
