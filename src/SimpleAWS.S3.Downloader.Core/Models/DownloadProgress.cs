namespace SimpleAWS.S3.Downloader.Core.Models;

/// <summary>
/// Represents progress information for a download operation.
/// </summary>
public sealed record DownloadProgress
{
    /// <summary>
    /// Gets the S3 key of the file being downloaded.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the total size of the file in bytes.
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// Gets the number of bytes downloaded so far.
    /// </summary>
    public long DownloadedBytes { get; init; }

    /// <summary>
    /// Gets the local file path where the file is being saved.
    /// </summary>
    public required string LocalFilePath { get; init; }

    /// <summary>
    /// Gets a value indicating whether the download is complete.
    /// </summary>
    public bool IsComplete { get; init; }

    /// <summary>
    /// Gets the error message if the download failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
