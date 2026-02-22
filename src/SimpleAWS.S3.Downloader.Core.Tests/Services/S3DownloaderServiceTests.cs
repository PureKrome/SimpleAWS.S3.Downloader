using Amazon.S3;
using Microsoft.Extensions.Logging;
using Moq;
using SimpleAWS.S3.Downloader.Core.Models;
using SimpleAWS.S3.Downloader.Core.Services;

namespace SimpleAWS.S3.Downloader.Tests;

public sealed class S3DownloaderServiceListObjectsAsyncTests
{
    [Fact]
    public async Task ListObjectsAsync_WithNullBucketName_ThrowsArgumentNullException()
    {
        // Arrange.
        var s3ClientMock = new Mock<IAmazonS3>();
        var loggerMock = new Mock<ILogger<S3DownloaderService>>();
        var service = new S3DownloaderService(s3ClientMock.Object, loggerMock.Object);

        // Act & Assert.
        await Should.ThrowAsync<ArgumentNullException>(() =>
            service.ListObjectsAsync(null!, cancellationToken: CancellationToken.None));
    }

    [Fact]
    public void S3DownloaderService_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange.
        var s3ClientMock = new Mock<IAmazonS3>();

        // Act & Assert.
        Should.Throw<ArgumentNullException>(() => new S3DownloaderService(s3ClientMock.Object, null!));
    }

    [Fact]
    public void S3DownloaderService_WithNullS3Client_ThrowsArgumentNullException()
    {
        // Arrange.
        var loggerMock = new Mock<ILogger<S3DownloaderService>>();

        // Act & Assert.
        Should.Throw<ArgumentNullException>(() => new S3DownloaderService(null!, loggerMock.Object));
    }
}

public sealed class S3DownloaderServiceDownloadBucketAsyncTests
{
    [Fact]
    public async Task DownloadBucketAsync_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange.
        var s3ClientMock = new Mock<IAmazonS3>();
        var loggerMock = new Mock<ILogger<S3DownloaderService>>();
        var service = new S3DownloaderService(s3ClientMock.Object, loggerMock.Object);

        // Act & Assert.
        await Should.ThrowAsync<ArgumentNullException>(() =>
            service.DownloadBucketAsync(null!, cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task DownloadBucketAsync_WithValidOptions_ReturnsDownloadResult()
    {
        // Arrange.
        var s3ClientMock = new Mock<IAmazonS3>();
        var loggerMock = new Mock<ILogger<S3DownloaderService>>();
        var service = new S3DownloaderService(s3ClientMock.Object, loggerMock.Object);
        
        var options = new DownloadOptions
        {
            BucketName = "test-bucket",
            LocalPath = Path.Combine(Path.GetTempPath(), "test-download"),
            MaxConcurrency = 1
        };

        s3ClientMock
            .Setup(x => x.ListObjectsV2Async(It.IsAny<Amazon.S3.Model.ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Amazon.S3.Model.ListObjectsV2Response
            {
                S3Objects = new List<Amazon.S3.Model.S3Object>()
            });

        // Act.
        var result = await service.DownloadBucketAsync(
            options,
            cancellationToken: CancellationToken.None);

        // Assert.
        result.ShouldNotBeNull();
        result.ShouldBeOfType<DownloadResult>();
        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(0);
        result.TotalBytesDownloaded.ShouldBe(0);
    }
}
