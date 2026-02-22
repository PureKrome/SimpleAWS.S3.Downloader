using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using SimpleAWS.S3.Downloader.Core.Models;
using System.Collections.Concurrent;

namespace SimpleAWS.S3.Downloader.Core.Services;

/// <summary>
/// Service for downloading files from AWS S3 buckets.
/// </summary>
public sealed class S3DownloaderService : IS3DownloaderService
{
    private readonly IAmazonS3 _s3Client;
    private readonly ILogger<S3DownloaderService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="S3DownloaderService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when logger is null.</exception>
    public S3DownloaderService(IAmazonS3 s3Client, ILogger<S3DownloaderService> logger)
    {
        _s3Client = s3Client ?? throw new ArgumentNullException(nameof(s3Client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<DownloadResult> DownloadBucketAsync(
        DownloadOptions options,
        Action<DownloadProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        _logger.LogInformation(
            "Starting download from bucket '{BucketName}' with prefix '{Prefix}' to '{LocalPath}'.",
            options.BucketName,
            options.Prefix ?? "(none)",
            options.LocalPath);

        var objectKeys = await ListObjectsInternalAsync(
            _s3Client,
            options.BucketName,
            options.Prefix,
            cancellationToken).ConfigureAwait(false);

        if (objectKeys.Count == 0)
        {
            _logger.LogWarning("No objects found in bucket '{BucketName}' with prefix '{Prefix}'.",
                options.BucketName,
                options.Prefix ?? "(none)");

            return new DownloadResult
            {
                SuccessCount = 0,
                FailureCount = 0,
                TotalBytesDownloaded = 0,
                Failures = Array.Empty<(string, string)>()
            };
        }

        _logger.LogInformation("Found {Count} objects to download.", objectKeys.Count);

        Directory.CreateDirectory(options.LocalPath);

        var failures = new ConcurrentBag<(string Key, string Error)>();
        var successCount = 0;
        var failureCount = 0;
        var totalBytesDownloaded = 0L;

        var semaphore = new SemaphoreSlim(options.MaxConcurrency, options.MaxConcurrency);
        var downloadTasks = objectKeys.Select(async key =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var localFilePath = Path.Combine(options.LocalPath, key.Replace('/', Path.DirectorySeparatorChar));
                var localDirectory = Path.GetDirectoryName(localFilePath);

                if (string.IsNullOrEmpty(localDirectory))
                {
                    _logger.LogWarning("Invalid local path for key '{Key}'. Skipping.", key);
                    Interlocked.Increment(ref failureCount);
                    failures.Add((key, "Invalid local path."));
                    return;
                }

                Directory.CreateDirectory(localDirectory);

                if (!options.OverwriteExisting && File.Exists(localFilePath))
                {
                    _logger.LogDebug("File '{FilePath}' already exists. Skipping.", localFilePath);
                    Interlocked.Increment(ref successCount);
                    return;
                }

                try
                {
                    var request = new GetObjectRequest
                    {
                        BucketName = options.BucketName,
                        Key = key
                    };

                    using var response = await _s3Client.GetObjectAsync(request, cancellationToken).ConfigureAwait(false);
                    var fileSize = response.ContentLength;

                    progressCallback?.Invoke(new DownloadProgress
                    {
                        Key = key,
                        TotalBytes = fileSize,
                        DownloadedBytes = 0,
                        LocalFilePath = localFilePath,
                        IsComplete = false
                    });

                    using var fileStream = new FileStream(localFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await response.ResponseStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);

                    Interlocked.Add(ref totalBytesDownloaded, fileSize);
                    Interlocked.Increment(ref successCount);

                    progressCallback?.Invoke(new DownloadProgress
                    {
                        Key = key,
                        TotalBytes = fileSize,
                        DownloadedBytes = fileSize,
                        LocalFilePath = localFilePath,
                        IsComplete = true
                    });

                    _logger.LogDebug("Downloaded '{Key}' to '{FilePath}' ({Size} bytes).",
                        key,
                        localFilePath,
                        fileSize);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to download '{Key}'.", key);
                    Interlocked.Increment(ref failureCount);
                    failures.Add((key, ex.Message));

                    progressCallback?.Invoke(new DownloadProgress
                    {
                        Key = key,
                        TotalBytes = 0,
                        DownloadedBytes = 0,
                        LocalFilePath = localFilePath,
                        IsComplete = false,
                        ErrorMessage = ex.Message
                    });
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(downloadTasks).ConfigureAwait(false);

        _logger.LogInformation(
            "Download complete. Success: {SuccessCount}, Failures: {FailureCount}, Total bytes: {TotalBytes}.",
            successCount,
            failureCount,
            totalBytesDownloaded);

        return new DownloadResult
        {
            SuccessCount = successCount,
            FailureCount = failureCount,
            TotalBytesDownloaded = totalBytesDownloaded,
            Failures = failures.ToArray()
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ListObjectsAsync(
        string bucketName,
        string? prefix = null,
        string? region = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bucketName);

        return await ListObjectsInternalAsync(_s3Client, bucketName, prefix, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<string>> ListObjectsInternalAsync(
        IAmazonS3 s3Client,
        string bucketName,
        string? prefix,
        CancellationToken cancellationToken)
    {
        var objectKeys = new List<string>();
        var request = new ListObjectsV2Request
        {
            BucketName = bucketName,
            Prefix = prefix
        };

        ListObjectsV2Response response;
        do
        {
            response = await s3Client.ListObjectsV2Async(request, cancellationToken).ConfigureAwait(false);
            objectKeys.AddRange(response.S3Objects.Where(obj => !obj.Key.EndsWith('/')).Select(obj => obj.Key));
            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated == true);

        return objectKeys;
    }
}
