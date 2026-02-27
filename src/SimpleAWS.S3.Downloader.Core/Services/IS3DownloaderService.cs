using SimpleAWS.S3.Downloader.Core.Models;

namespace SimpleAWS.S3.Downloader.Core.Services;

/// <summary>
/// Service interface for downloading files from AWS S3 buckets.
/// </summary>
public interface IS3DownloaderService
{
    /// <summary>
    /// Downloads files from an S3 bucket to a local directory.
    /// </summary>
    /// <param name="options">The download options.</param>
    /// <param name="progressCallback">Optional callback for progress updates.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation with the download result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the bucket does not exist or is not accessible.</exception>
    Task<DownloadResult> DownloadBucketAsync(
        DownloadOptions options,
        Action<DownloadProgress>? progressCallback = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all objects in an S3 bucket with optional prefix filtering.
    /// </summary>
    /// <param name="bucketName">The name of the S3 bucket.</param>
    /// <param name="prefix">Optional prefix to filter objects.</param>
    /// <param name="region">Optional AWS region.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation with the list of S3 object keys.</returns>
    /// <exception cref="ArgumentNullException">Thrown when bucketName is null.</exception>
    Task<IReadOnlyList<string>> ListObjectsAsync(
        string bucketName,
        string? prefix = null,
        string? region = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a summary of the objects in an S3 bucket (count and total size).
    /// </summary>
    /// <param name="bucketName">The name of the S3 bucket.</param>
    /// <param name="prefix">Optional prefix to filter objects.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation with the bucket summary.</returns>
    /// <exception cref="ArgumentNullException">Thrown when bucketName is null.</exception>
    Task<BucketSummary> GetBucketSummaryAsync(
        string bucketName,
        string? prefix = null,
        CancellationToken cancellationToken = default);
}
