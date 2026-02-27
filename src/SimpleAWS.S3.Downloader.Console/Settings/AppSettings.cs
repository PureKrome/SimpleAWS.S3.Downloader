namespace SimpleAWS.S3.Downloader.Console.Settings;

/// <summary>
/// Application-level settings (non-AWS).
/// </summary>
public sealed record AppSettings
{
    /// <summary>
    /// Gets the default local folder path used for downloads.
    /// When <see langword="null"/>, the current working directory is used.
    /// </summary>
    public string? DefaultDownloadPath { get; init; }
}
