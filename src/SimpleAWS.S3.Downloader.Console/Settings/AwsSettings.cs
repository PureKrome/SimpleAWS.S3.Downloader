namespace SimpleAWS.S3.Downloader.Console.Settings;

public sealed record AwsSettings
{
    public string? Profile { get; init; }

    public string? Region { get; init; }
}
