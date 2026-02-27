using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.Runtime.CredentialManagement;

namespace SimpleAWS.S3.Downloader.Console.Settings;

/// <summary>
/// Reads AWS SSO token information from the local SSO cache directory.
/// </summary>
public sealed class SsoTokenReader
{
    private static readonly string SsoCacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".aws",
        "sso",
        "cache");

    /// <summary>
    /// Reads the authentication / SSO token status for the given AWS profile.
    /// </summary>
    /// <param name="profileName">The AWS profile name to look up.</param>
    /// <returns>A result describing the auth type and, when applicable, the SSO token state.</returns>
    public SsoTokenLookupResult ReadAuthStatus(string? profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return new SsoTokenLookupResult(ProfileAuthKind.NoProfile, null);
        }

        var (sessionName, startUrl) = GetSsoDetailsFromProfile(profileName);

        if (sessionName is null && startUrl is null)
        {
            return new SsoTokenLookupResult(ProfileAuthKind.NonSso, null);
        }

        // Try session-based cache first (sso-session config), then legacy start URL.
        var tokenInfo = TryReadFromCache(sessionName) ?? TryReadFromCache(startUrl);

        return tokenInfo is not null
            ? new SsoTokenLookupResult(ProfileAuthKind.Sso, tokenInfo)
            : new SsoTokenLookupResult(ProfileAuthKind.SsoCacheMissing, null);
    }

    /// <summary>
    /// Reads SSO session/start URL details from the AWS credential profile.
    /// </summary>
    private static (string? SessionName, string? StartUrl) GetSsoDetailsFromProfile(string profileName)
    {
        try
        {
            var chain = new CredentialProfileStoreChain();
            if (!chain.TryGetProfile(profileName, out var profile))
            {
                return (null, null);
            }

            return (profile.Options.SsoSession, profile.Options.SsoStartUrl);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Tries to read token info from the SSO cache using a cache key (session name or start URL).
    /// </summary>
    private static SsoTokenInfo? TryReadFromCache(string? cacheKey)
    {
        if (string.IsNullOrWhiteSpace(cacheKey))
        {
            return null;
        }

        var hash = ComputeSha1Hex(cacheKey);
        var cacheFilePath = Path.Combine(SsoCacheDirectory, $"{hash}.json");

        if (!File.Exists(cacheFilePath))
        {
            return null;
        }

        return TryParseCacheFile(cacheFilePath);
    }

    /// <summary>
    /// Parses an SSO cache JSON file and extracts the expiry time.
    /// </summary>
    private static SsoTokenInfo? TryParseCacheFile(string filePath)
    {
        try
        {
            var json = File.ReadAllBytes(filePath);
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("expiresAt", out var expiresAtElement))
            {
                return null;
            }

            var expiresAtString = expiresAtElement.GetString();
            if (string.IsNullOrWhiteSpace(expiresAtString))
            {
                return null;
            }

            // The AWS CLI writes expiresAt in formats like "2024-01-15T10:30:00UTC" or ISO 8601.
            expiresAtString = expiresAtString
                .Replace("UTC", "Z", StringComparison.OrdinalIgnoreCase);

            if (!DateTimeOffset.TryParse(expiresAtString, out var expiresAt))
            {
                return null;
            }

            return new SsoTokenInfo { ExpiresAt = expiresAt };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Computes a lowercase hex SHA1 hash of the given input string (UTF-8).
    /// This matches the cache key derivation used by the AWS CLI.
    /// </summary>
    private static string ComputeSha1Hex(string input)
    {
        var inputBytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA1.HashData(inputBytes);
        return Convert.ToHexStringLower(hashBytes);
    }
}

/// <summary>
/// Describes how an AWS profile authenticates.
/// </summary>
public enum ProfileAuthKind
{
    /// <summary>No profile is configured.</summary>
    NoProfile,

    /// <summary>The profile does not use SSO (e.g. static IAM keys, assumed role).</summary>
    NonSso,

    /// <summary>The profile uses SSO and a cached token was found.</summary>
    Sso,

    /// <summary>The profile uses SSO but no cached token was found.</summary>
    SsoCacheMissing
}

/// <summary>
/// The result of looking up SSO token information for a profile.
/// </summary>
/// <param name="AuthKind">The authentication type of the profile.</param>
/// <param name="TokenInfo">The SSO token details, if available.</param>
public sealed record SsoTokenLookupResult(ProfileAuthKind AuthKind, SsoTokenInfo? TokenInfo)
{
    /// <summary>
    /// Gets a human-readable status string suitable for display.
    /// </summary>
    public string StatusText => AuthKind switch
    {
        ProfileAuthKind.NoProfile => "No profile configured",
        ProfileAuthKind.NonSso => "Static credentials (no SSO)",
        ProfileAuthKind.SsoCacheMissing => "SSO token not found – run aws sso login",
        ProfileAuthKind.Sso => TokenInfo?.StatusText ?? "Unknown",
        _ => "Unknown"
    };
}
