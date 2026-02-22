namespace SimpleAWS.S3.Downloader.Core.Models;

/// <summary>
/// Represents the result of a download operation.
/// </summary>
public sealed record DownloadResult
{
    /// <summary>
    /// Gets the total number of files downloaded successfully.
    /// </summary>
    public int SuccessCount { get; init; }

    /// <summary>
    /// Gets the total number of files that failed to download.
    /// </summary>
    public int FailureCount { get; init; }

    /// <summary>
    /// Gets the total number of bytes downloaded.
    /// </summary>
    public long TotalBytesDownloaded { get; init; }

    /// <summary>
    /// Gets the list of files that failed to download with their error messages.
    /// </summary>
    public required IReadOnlyList<(string Key, string Error)> Failures { get; init; }

    /// <summary>
    /// Gets a value indicating whether all downloads were successful.
    /// </summary>
    public bool IsSuccess => FailureCount == 0;
}
