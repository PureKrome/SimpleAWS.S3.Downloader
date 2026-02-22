using System.Text.Json;

namespace SimpleAWS.S3.Downloader.Console.Settings;

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _appSettingsPath;

    public AppSettingsStore(string appSettingsPath)
    {
        _appSettingsPath = !string.IsNullOrWhiteSpace(appSettingsPath)
            ? appSettingsPath
            : throw new ArgumentNullException(nameof(appSettingsPath));
    }

    public AwsSettings LoadAwsSettings()
    {
        if (!File.Exists(_appSettingsPath))
        {
            return new AwsSettings();
        }

        using var stream = File.OpenRead(_appSettingsPath);
        using var document = JsonDocument.Parse(stream);

        if (!document.RootElement.TryGetProperty("AWS", out var awsElement))
        {
            return new AwsSettings();
        }

        string? profile = null;
        if (awsElement.TryGetProperty("Profile", out var profileElement) && profileElement.ValueKind == JsonValueKind.String)
        {
            profile = profileElement.GetString();
        }

        string? region = null;
        if (awsElement.TryGetProperty("Region", out var regionElement) && regionElement.ValueKind == JsonValueKind.String)
        {
            region = regionElement.GetString();
        }

        return new AwsSettings
        {
            Profile = profile,
            Region = region
        };
    }

    public void SaveAwsSettings(AwsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var root = new Dictionary<string, object?>();

        if (File.Exists(_appSettingsPath))
        {
            var existingJson = File.ReadAllText(_appSettingsPath);
            if (!string.IsNullOrWhiteSpace(existingJson))
            {
                var existing = JsonSerializer.Deserialize<Dictionary<string, object?>>(existingJson);
                if (existing is not null)
                {
                    root = existing;
                }
            }
        }

        root["AWS"] = new Dictionary<string, object?>
        {
            ["Profile"] = settings.Profile,
            ["Region"] = settings.Region
        };

        var updatedJson = JsonSerializer.Serialize(root, SerializerOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(_appSettingsPath) ?? AppContext.BaseDirectory);
        File.WriteAllText(_appSettingsPath, updatedJson);
    }
}
