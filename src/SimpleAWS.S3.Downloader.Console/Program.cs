using Amazon.S3;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SimpleAWS.S3.Downloader.Console;
using SimpleAWS.S3.Downloader.Core.Services;
using SimpleAWS.S3.Downloader.Console.Settings;

static string? TryGetCliOption(string[] args, string optionName)
{
    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        if (string.Equals(arg, optionName, StringComparison.OrdinalIgnoreCase))
        {
            return i + 1 < args.Length ? args[i + 1] : null;
        }

        if (arg.StartsWith(optionName + "=", StringComparison.OrdinalIgnoreCase))
        {
            return arg[(optionName.Length + 1)..];
        }
    }

    return null;
}

var services = new ServiceCollection();

var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
services.AddSingleton(_ => new AppSettingsStore(appSettingsPath));

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

services.AddSingleton<IConfiguration>(configuration);

var configProfile = configuration["AWS:Profile"];
var configRegion = configuration["AWS:Region"] ?? configuration["AWS_REGION"];
var cliProfile = TryGetCliOption(args, "--profile");
var resolvedProfile = string.IsNullOrWhiteSpace(cliProfile) ? configProfile : cliProfile;
var resolvedRegion = configRegion;

AwsSettings ResolveAwsSettings(AppSettingsStore store)
{
    var persisted = store.LoadAwsSettings();

    var profile = string.IsNullOrWhiteSpace(resolvedProfile) ? persisted.Profile : resolvedProfile;
    var region = string.IsNullOrWhiteSpace(resolvedRegion) ? persisted.Region : resolvedRegion;

    return new AwsSettings
    {
        Profile = string.IsNullOrWhiteSpace(profile) ? null : profile,
        Region = string.IsNullOrWhiteSpace(region) ? null : region
    };
}

services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Warning);
});

services.AddSingleton<Func<AwsSettings, IAmazonS3>>(_ =>
{
    return settings =>
    {
        if (string.IsNullOrWhiteSpace(settings.Profile))
        {
            if (string.IsNullOrWhiteSpace(settings.Region))
            {
                return new AmazonS3Client();
            }

            var regionEndpoint = Amazon.RegionEndpoint.GetBySystemName(settings.Region);
            return new AmazonS3Client(regionEndpoint);
        }

        var chain = new CredentialProfileStoreChain();
        if (!chain.TryGetProfile(settings.Profile, out var profile))
        {
            throw new InvalidOperationException(
                $"AWS profile '{settings.Profile}' was not found. Set AWS:Profile in appsettings.json or pass --profile <name>.");
        }

        if (!chain.TryGetAWSCredentials(settings.Profile, out var credentials))
        {
            throw new InvalidOperationException(
                $"AWS profile '{settings.Profile}' was not found. Set AWS:Profile in appsettings.json or pass --profile <name>.");
        }

        var regionalEndpoint = !string.IsNullOrWhiteSpace(settings.Region)
            ? Amazon.RegionEndpoint.GetBySystemName(settings.Region)
            : profile.Region;

        if (regionalEndpoint is null)
        {
            throw new InvalidOperationException(
                "No AWS region configured. Specify a region via AWS:Region in appsettings.json, set AWS_REGION, or add region to the selected AWS profile.");
        }

        return new AmazonS3Client(credentials, regionalEndpoint);
    };
});

services.AddSingleton(provider =>
{
    var store = provider.GetRequiredService<AppSettingsStore>();
    var factory = provider.GetRequiredService<Func<AwsSettings, IAmazonS3>>();
    var settings = ResolveAwsSettings(store);
    return factory(settings);
});
services.AddSingleton<IS3DownloaderService, S3DownloaderService>();
services.AddSingleton<Application>();

var serviceProvider = services.BuildServiceProvider();
var app = serviceProvider.GetRequiredService<Application>();

try
{
    await app.RunAsync(args);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}
