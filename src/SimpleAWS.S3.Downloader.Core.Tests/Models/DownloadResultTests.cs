using SimpleAWS.S3.Downloader.Core.Models;
using Shouldly;

namespace SimpleAWS.S3.Downloader.Tests;

public sealed class DownloadResultTests
{
    [Fact]
    public void DownloadResult_WithSuccessfulDownloads_IsSuccessReturnsTrue()
    {
        // Arrange.
        var successCount = 5;
        var failureCount = 0;
        var totalBytes = 5120L;

        // Act.
        var result = new DownloadResult
        {
            SuccessCount = successCount,
            FailureCount = failureCount,
            TotalBytesDownloaded = totalBytes,
            Failures = Array.Empty<(string, string)>()
        };

        // Assert.
        result.SuccessCount.ShouldBe(successCount);
        result.FailureCount.ShouldBe(failureCount);
        result.TotalBytesDownloaded.ShouldBe(totalBytes);
        result.Failures.ShouldBeEmpty();
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void DownloadResult_WithFailures_IsSuccessReturnsFalse()
    {
        // Arrange.
        var successCount = 3;
        var failureCount = 2;
        var failures = new[] { ("file1.txt", "Access denied"), ("file2.txt", "Not found") };

        // Act.
        var result = new DownloadResult
        {
            SuccessCount = successCount,
            FailureCount = failureCount,
            TotalBytesDownloaded = 3072L,
            Failures = failures
        };

        // Assert.
        result.SuccessCount.ShouldBe(successCount);
        result.FailureCount.ShouldBe(failureCount);
        result.Failures.Count.ShouldBe(2);
        result.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void DownloadResult_WithZeroDownloads_ReturnsSuccess()
    {
        // Arrange & Act.
        var result = new DownloadResult
        {
            SuccessCount = 0,
            FailureCount = 0,
            TotalBytesDownloaded = 0L,
            Failures = Array.Empty<(string, string)>()
        };

        // Assert.
        result.IsSuccess.ShouldBeTrue();
        result.Failures.ShouldBeEmpty();
    }
}
