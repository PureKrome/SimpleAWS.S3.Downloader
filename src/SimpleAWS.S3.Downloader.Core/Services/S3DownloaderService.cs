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
                Failures = []
            };
        }

        _logger.LogInformation("Found {Count} objects to download.", objectKeys.Count);

        Directory.CreateDirectory(options.LocalPath);

        var counters = new DownloadCounters();

        try
        {
            await Parallel.ForEachAsync(
                objectKeys,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = options.MaxConcurrency,
                    CancellationToken = cancellationToken
                },
                (key, ct) => DownloadObjectAsync(key, options, counters, progressCallback, ct))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Download cancelled. Success: {SuccessCount}, Failures: {FailureCount}, Total bytes: {TotalBytes}.",
                counters.SuccessCount,
                counters.FailureCount,
                counters.TotalBytesDownloaded);
        }

        _logger.LogInformation(
            "Download complete. Success: {SuccessCount}, Failures: {FailureCount}, Total bytes: {TotalBytes}.",
            counters.SuccessCount,
            counters.FailureCount,
            counters.TotalBytesDownloaded);

        return new DownloadResult
        {
            SuccessCount = counters.SuccessCount,
            FailureCount = counters.FailureCount,
            TotalBytesDownloaded = counters.TotalBytesDownloaded,
            Failures = counters.Failures.ToArray()
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

    /// <inheritdoc/>
    public async Task<BucketSummary> GetBucketSummaryAsync(
        string bucketName,
        string? prefix = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bucketName);

        var request = new ListObjectsV2Request
        {
            BucketName = bucketName,
            Prefix = prefix
        };

        var objectCount = 0;
        var totalSize = 0L;

        ListObjectsV2Response response;
        do
        {
            response = await _s3Client.ListObjectsV2Async(request, cancellationToken).ConfigureAwait(false);

            foreach (var obj in response.S3Objects ?? Enumerable.Empty<S3Object>())
            {
                if (!obj.Key.EndsWith('/'))
                {
                    objectCount++;
                    totalSize += obj.Size ?? 0;
                }
            }

            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated == true);

        return new BucketSummary
        {
            ObjectCount = objectCount,
            TotalSizeBytes = totalSize
        };
    }

    private async ValueTask DownloadObjectAsync(
        string key,
        DownloadOptions options,
        DownloadCounters counters,
        Action<DownloadProgress>? progressCallback,
        CancellationToken ct)
    {
        var localFilePath = Path.Combine(options.LocalPath, key.Replace('/', Path.DirectorySeparatorChar));
        var localDirectory = Path.GetDirectoryName(localFilePath);

        if (string.IsNullOrWhiteSpace(localDirectory))
        {
            _logger.LogWarning("Invalid local path for key '{Key}'. Skipping.", key);
            Interlocked.Increment(ref counters.FailureCount);
            counters.Failures.Add((key, "Invalid local path."));
            return;
        }

        Directory.CreateDirectory(localDirectory);

        if (!options.OverwriteExisting && File.Exists(localFilePath))
        {
            _logger.LogDebug("File '{FilePath}' already exists. Skipping.", localFilePath);
            Interlocked.Increment(ref counters.SuccessCount);
            return;
        }

        try
        {
            var request = new GetObjectRequest
            {
                BucketName = options.BucketName,
                Key = key
            };

            using var response = await _s3Client
                .GetObjectAsync(request, ct)
                .ConfigureAwait(false);
            var fileSize = response.ContentLength;

            var progress = new DownloadProgress
            {
                Key = key,
                TotalBytes = fileSize,
                DownloadedBytes = 0,
                LocalFilePath = localFilePath,
                IsComplete = false
            };
            progressCallback?.Invoke(progress);

            using var fileStream = new FileStream(localFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.ResponseStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);

            Interlocked.Add(ref counters.TotalBytesDownloaded, fileSize);
            Interlocked.Increment(ref counters.SuccessCount);

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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // User requested cancellation — Parallel.ForEachAsync will stop gracefully.
            _logger.LogInformation("User cancelled download of '{Key}'.", key);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download '{Key}'.", key);
            Interlocked.Increment(ref counters.FailureCount);
            counters.Failures.Add((key, ex.Message));

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
            objectKeys.AddRange((response.S3Objects ?? []).Where(obj => !obj.Key.EndsWith('/')).Select(obj => obj.Key));
            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated == true);

        return objectKeys;
    }

    /// <summary>
    /// Thread-safe counters shared across parallel download tasks.
    /// </summary>
    private sealed class DownloadCounters
    {
        public int SuccessCount;
        public int FailureCount;
        public long TotalBytesDownloaded;
        public readonly ConcurrentBag<(string Key, string Error)> Failures = new();
    }
}
