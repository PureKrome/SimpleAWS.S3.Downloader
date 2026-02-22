using SimpleAWS.S3.Downloader.Core.Models;

namespace SimpleAWS.S3.Downloader.Tests;

public sealed class DownloadProgressTests
{
    [Fact]
    public void DownloadProgress_WithValidData_CreatesSuccessfully()
    {
        // Arrange.
        var key = "folder/file.txt";
        var localFilePath = "/downloads/folder/file.txt";
        var totalBytes = 1024L;
        var downloadedBytes = 512L;

        // Act.
        var progress = new DownloadProgress
        {
            Key = key,
            LocalFilePath = localFilePath,
            TotalBytes = totalBytes,
            DownloadedBytes = downloadedBytes,
            IsComplete = false
        };

        // Assert.
        progress.Key.ShouldBe(key);
        progress.LocalFilePath.ShouldBe(localFilePath);
        progress.TotalBytes.ShouldBe(totalBytes);
        progress.DownloadedBytes.ShouldBe(downloadedBytes);
        progress.IsComplete.ShouldBeFalse();
        progress.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public void DownloadProgress_WithError_StoresErrorMessage()
    {
        // Arrange.
        var key = "folder/file.txt";
        var localFilePath = "/downloads/folder/file.txt";
        var errorMessage = "Access denied";

        // Act.
        var progress = new DownloadProgress
        {
            Key = key,
            LocalFilePath = localFilePath,
            TotalBytes = 0,
            DownloadedBytes = 0,
            IsComplete = false,
            ErrorMessage = errorMessage
        };

        // Assert.
        progress.Key.ShouldBe(key);
        progress.IsComplete.ShouldBeFalse();
        progress.ErrorMessage.ShouldBe(errorMessage);
    }

    [Fact]
    public void DownloadProgress_WhenComplete_DownloadedBytesEqualsTotal()
    {
        // Arrange.
        var key = "folder/file.txt";
        var localFilePath = "/downloads/folder/file.txt";
        var totalBytes = 1024L;

        // Act.
        var progress = new DownloadProgress
        {
            Key = key,
            LocalFilePath = localFilePath,
            TotalBytes = totalBytes,
            DownloadedBytes = totalBytes,
            IsComplete = true
        };

        // Assert.
        progress.TotalBytes.ShouldBe(totalBytes);
        progress.DownloadedBytes.ShouldBe(progress.TotalBytes);
        progress.IsComplete.ShouldBeTrue();
    }
}
