using SimpleAWS.S3.Downloader.Core.Models;

namespace SimpleAWS.S3.Downloader.Tests;

public sealed class DownloadOptionsTests
{
    [Fact]
    public void DownloadOptions_WithValidData_CreatesSuccessfully()
    {
        // Arrange.
        var bucketName = "test-bucket";
        var localPath = "/downloads";

        // Act.
        var options = new DownloadOptions
        {
            BucketName = bucketName,
            LocalPath = localPath
        };

        // Assert.
        options.BucketName.ShouldBe(bucketName);
        options.LocalPath.ShouldBe(localPath);
        options.Prefix.ShouldBeNull();
        options.Region.ShouldBeNull();
        options.OverwriteExisting.ShouldBeFalse();
        options.MaxConcurrency.ShouldBe(5);
    }

    [Fact]
    public void DownloadOptions_WithAllProperties_CreatesSuccessfully()
    {
        // Arrange.
        var bucketName = "test-bucket";
        var localPath = "/downloads";
        var prefix = "folder/";
        var region = "us-east-1";
        var overwrite = true;
        var maxConcurrency = 10;

        // Act.
        var options = new DownloadOptions
        {
            BucketName = bucketName,
            LocalPath = localPath,
            Prefix = prefix,
            Region = region,
            OverwriteExisting = overwrite,
            MaxConcurrency = maxConcurrency
        };

        // Assert.
        options.BucketName.ShouldBe(bucketName);
        options.LocalPath.ShouldBe(localPath);
        options.Prefix.ShouldBe(prefix);
        options.Region.ShouldBe(region);
        options.OverwriteExisting.ShouldBeTrue();
        options.MaxConcurrency.ShouldBe(maxConcurrency);
    }

    [Fact]
    public void DownloadOptions_IsRecord_SupportsEquality()
    {
        // Arrange.
        var options1 = new DownloadOptions { BucketName = "bucket", LocalPath = "/path" };
        var options2 = new DownloadOptions { BucketName = "bucket", LocalPath = "/path" };

        // Act & Assert.
        options1.ShouldBe(options2);
    }
}
