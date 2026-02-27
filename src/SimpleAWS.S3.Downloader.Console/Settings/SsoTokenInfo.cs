namespace SimpleAWS.S3.Downloader.Console.Settings;

/// <summary>
/// Represents the current state of an AWS SSO token.
/// </summary>
public sealed record SsoTokenInfo
{
    /// <summary>
    /// Gets the UTC time when the token expires.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Gets a value indicating whether the token has expired.
    /// </summary>
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value <= DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the time remaining until the token expires, or <see langword="null"/> if already expired or unknown.
    /// </summary>
    public TimeSpan? TimeRemaining
    {
        get
        {
            if (!ExpiresAt.HasValue)
            {
                return null;
            }

            var remaining = ExpiresAt.Value - DateTimeOffset.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : null;
        }
    }

    /// <summary>
    /// Gets a human-readable summary of the token status.
    /// </summary>
    public string StatusText
    {
        get
        {
            if (!ExpiresAt.HasValue)
            {
                return "Unknown";
            }

            if (IsExpired)
            {
                var ago = DateTimeOffset.UtcNow - ExpiresAt.Value;
                return $"Expired ({FormatDuration(ago)} ago)";
            }

            var remaining = ExpiresAt.Value - DateTimeOffset.UtcNow;
            return $"Valid – expires in {FormatDuration(remaining)}";
        }
    }

    /// <summary>
    /// Formats a duration into a human-readable string.
    /// </summary>
    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
        {
            return $"{(int)duration.TotalDays}d {duration.Hours}h";
        }

        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        return $"{(int)duration.TotalMinutes}m";
    }
}
