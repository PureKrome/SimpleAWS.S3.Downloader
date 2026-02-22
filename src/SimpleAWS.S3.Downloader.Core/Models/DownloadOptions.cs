namespace SimpleAWS.S3.Downloader.Core.Models;

/// <summary>
/// Options for downloading S3 bucket contents.
/// </summary>
public sealed record DownloadOptions
{
    /// <summary>
    /// Gets the name of the S3 bucket to download from.
    /// </summary>
    public required string BucketName { get; init; }

    /// <summary>
    /// Gets the AWS region where the bucket is located.
    /// </summary>
    public string? Region { get; init; }

    /// <summary>
    /// Gets the local directory path where files will be downloaded.
    /// </summary>
    public required string LocalPath { get; init; }

    /// <summary>
    /// Gets the S3 key prefix to filter which objects to download.
    /// </summary>
    public string? Prefix { get; init; }

    /// <summary>
    /// Gets a value indicating whether to overwrite existing local files.
    /// </summary>
    public bool OverwriteExisting { get; init; }

    /// <summary>
    /// Gets the maximum number of concurrent downloads.
    /// </summary>
    public int MaxConcurrency { get; init; } = 5;
}
