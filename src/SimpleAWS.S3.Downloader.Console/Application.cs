using SimpleAWS.S3.Downloader.Core.Models;
using SimpleAWS.S3.Downloader.Core.Services;
using Spectre.Console;
using System.Diagnostics;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using SimpleAWS.S3.Downloader.Console.Settings;

namespace SimpleAWS.S3.Downloader.Console;

/// <summary>
/// Main application class for the S3 downloader console application.
/// </summary>
public sealed class Application
{
    private readonly IS3DownloaderService _downloaderService;
    private readonly AppSettingsStore _settingsStore;
    private readonly Func<AwsSettings, IAmazonS3> _s3Factory;
    private readonly AwsSettings _startupAwsSettings;

    /// <summary>
    /// Initializes a new instance of the <see cref="Application"/> class.
    /// </summary>
    /// <param name="downloaderService">The S3 downloader service.</param>
    /// <exception cref="ArgumentNullException">Thrown when downloaderService is null.</exception>
    public Application(
        IS3DownloaderService downloaderService,
        AppSettingsStore settingsStore,
        Func<AwsSettings, IAmazonS3> s3Factory,
        IConfiguration configuration)
    {
        _downloaderService = downloaderService ?? throw new ArgumentNullException(nameof(downloaderService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _s3Factory = s3Factory ?? throw new ArgumentNullException(nameof(s3Factory));

        ArgumentNullException.ThrowIfNull(configuration);
        _startupAwsSettings = new AwsSettings
        {
            Profile = configuration["AWS:Profile"],
            Region = configuration["AWS:Region"] ?? configuration["AWS_REGION"]
        };
    }

    /// <summary>
    /// Runs the application with the provided command-line arguments.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            RenderHeader(clearScreen: true);

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[green]Select an option:[/]")
                    .PageSize(10)
                    .AddChoices(
                        "Change config settings",
                        "Backup a single bucket",
                        "Backup all buckets",
                        "Exit"));

            switch (choice)
            {
                case "Change config settings":
                    RenderHeader(clearScreen: true);
                    ChangeConfigSettings();
                    break;

                case "Backup a single bucket":
                    RenderHeader(clearScreen: true);
                    await BackupSingleBucketAsync(cancellationToken).ConfigureAwait(false);
                    break;

                case "Backup all buckets":
                    RenderHeader(clearScreen: true);
                    await BackupAllBucketsAsync(cancellationToken).ConfigureAwait(false);
                    break;

                case "Exit":
                    return;
            }

            AnsiConsole.MarkupLine("\n[grey]Press any key to return to the main menu...[/]");
            System.Console.ReadKey(intercept: true);
        }
    }

    private void RenderHeader(bool clearScreen)
    {
        if (clearScreen)
        {
            AnsiConsole.Clear();
        }

        DisplayBanner();
        DisplayCurrentSettings();
    }

    private void DisplayCurrentSettings()
    {
        var current = _settingsStore.LoadAwsSettings();
        var effectiveProfile = current.Profile ?? _startupAwsSettings.Profile;
        var effectiveRegion = current.Region ?? _startupAwsSettings.Region;

        var panel = new Panel(
            new Markup(
                $"[yellow]Profile:[/] [cyan]{EscapeMarkup(effectiveProfile ?? "(default)")}[/]\n" +
                $"[yellow]Region:[/] [cyan]{EscapeMarkup(effectiveRegion ?? "(from profile)")}[/]"))
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader("[grey]Current AWS Settings[/]", Justify.Left)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    private static string EscapeMarkup(string value) => Markup.Escape(value);

    private void ChangeConfigSettings()
    {
        var current = _settingsStore.LoadAwsSettings();
        var effectiveProfile = current.Profile ?? _startupAwsSettings.Profile;
        var effectiveRegion = current.Region ?? _startupAwsSettings.Region;

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[yellow]Setting[/]")
            .AddColumn("[yellow]Value[/]");

        table.AddRow("AWS Profile", $"[cyan]{effectiveProfile ?? "(default)"}[/]");
        table.AddRow("AWS Region", $"[cyan]{effectiveRegion ?? "(not set)"}[/]");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        var newProfile = AnsiConsole
            .Prompt(
                new TextPrompt<string>("[green]AWS profile (blank clears):[/]")
                    .AllowEmpty()
                    .DefaultValue(effectiveProfile ?? string.Empty)
                    .ShowDefaultValue(true));

        var newRegion = AnsiConsole
            .Prompt(
                new TextPrompt<string>("[green]AWS region override (blank clears):[/]")
                    .AllowEmpty()
                    .DefaultValue(effectiveRegion ?? string.Empty)
                    .ShowDefaultValue(true));

        var updated = new AwsSettings
        {
            Profile = string.IsNullOrWhiteSpace(newProfile) ? null : newProfile,
            Region = string.IsNullOrWhiteSpace(newRegion) ? null : newRegion
        };

        _settingsStore.SaveAwsSettings(updated);

        AnsiConsole.MarkupLine("[green]Settings saved to appsettings.json.[/]");
        AnsiConsole.MarkupLine("[dim]Note: The active S3 client is created from CLI/appsettings/env at startup. Restart the app to apply updated AWS settings.[/]");
    }

    private async Task BackupSingleBucketAsync(CancellationToken cancellationToken)
    {
        var buckets = await ListBucketsAsync(cancellationToken).ConfigureAwait(false);
        if (buckets.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No buckets found for the current credentials.[/]");
            return;
        }

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[green]Select a bucket to back up:[/]")
                .PageSize(15)
                .MoreChoicesText("[grey](Move up and down to reveal more buckets)[/]")
                .AddChoices(buckets));

        var options = await GetDownloadOptionsForBucketAsync(selected, cancellationToken).ConfigureAwait(false);
        if (options is null)
        {
            return;
        }

        await DownloadWithProgressAsync(options, cancellationToken).ConfigureAwait(false);
    }

    private async Task BackupAllBucketsAsync(CancellationToken cancellationToken)
    {
        var buckets = await ListBucketsAsync(cancellationToken).ConfigureAwait(false);
        if (buckets.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No buckets found for the current credentials.[/]");
            return;
        }

        var baseLocalPath = AnsiConsole.Ask("[green]Enter base local download path:[/]", Environment.CurrentDirectory);
        var overwriteExisting = AnsiConsole.Confirm("Overwrite existing files?", false);
        var maxConcurrency = AnsiConsole.Ask("[green]Max concurrent downloads:[/]", 5);

        if (!AnsiConsole.Confirm($"Proceed with downloading [cyan]{buckets.Count}[/] buckets?"))
        {
            AnsiConsole.MarkupLine("[red]Download cancelled.[/]");
            return;
        }

        foreach (var bucket in buckets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var localPath = Path.Combine(baseLocalPath, bucket);
            var options = new DownloadOptions
            {
                BucketName = bucket,
                Prefix = null,
                LocalPath = localPath,
                Region = null,
                OverwriteExisting = overwriteExisting,
                MaxConcurrency = maxConcurrency
            };

            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule($"[yellow]Bucket: {bucket}[/]").RuleStyle("grey").LeftJustified());

            await DownloadWithProgressAsync(options, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<string>> ListBucketsAsync(CancellationToken cancellationToken)
    {
        using var client = CreateS3ClientFromCurrentSettings();
        var response = await client.ListBucketsAsync(cancellationToken).ConfigureAwait(false);
        return response.Buckets
            .Select(b => b.BucketName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IAmazonS3 CreateS3ClientFromCurrentSettings()
    {
        var persisted = _settingsStore.LoadAwsSettings();
        var settings = new AwsSettings
        {
            Profile = persisted.Profile ?? _startupAwsSettings.Profile,
            Region = persisted.Region ?? _startupAwsSettings.Region
        };

        return _s3Factory(settings);
    }

    private static async Task<DownloadOptions?> GetDownloadOptionsForBucketAsync(string bucketName, CancellationToken cancellationToken)
    {
        var prefix = AnsiConsole.Confirm("Do you want to filter by prefix?")
            ? AnsiConsole.Ask<string>("[green]Enter prefix (e.g., 'folder/subfolder/'):[/]")
            : null;

        var localPath = AnsiConsole.Ask("[green]Enter local download path:[/]", Path.Combine(Environment.CurrentDirectory, bucketName));

        var overwriteExisting = AnsiConsole.Confirm("Overwrite existing files?", false);
        var maxConcurrency = AnsiConsole.Ask("[green]Max concurrent downloads:[/]", 5);

        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[yellow]Setting[/]")
            .AddColumn("[yellow]Value[/]");

        table.AddRow("Bucket Name", $"[cyan]{bucketName}[/]");
        table.AddRow("Prefix", $"[cyan]{prefix ?? "(none)"}[/]");
        table.AddRow("Local Path", $"[cyan]{localPath}[/]");
        table.AddRow("Overwrite Existing", $"[cyan]{overwriteExisting}[/]");
        table.AddRow("Max Concurrency", $"[cyan]{maxConcurrency}[/]");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        if (!AnsiConsole.Confirm("Proceed with download?"))
        {
            AnsiConsole.MarkupLine("[red]Download cancelled.[/]");
            return null;
        }

        return new DownloadOptions
        {
            BucketName = bucketName,
            Prefix = prefix,
            LocalPath = localPath,
            Region = null,
            OverwriteExisting = overwriteExisting,
            MaxConcurrency = maxConcurrency
        };
    }

    private static void DisplayBanner()
    {
        AnsiConsole.Write(
            new FigletText("S3 Downloader")
                .LeftJustified()
                .Color(Color.Cyan1));

        AnsiConsole.MarkupLine("[dim]Simple AWS S3 Bucket Downloader[/]");
        AnsiConsole.WriteLine();
    }

    private async Task DownloadWithProgressAsync(DownloadOptions options, CancellationToken cancellationToken)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Starting download...[/]");
        AnsiConsole.WriteLine();

        var stopwatch = Stopwatch.StartNew();
        var progressTasks = new Dictionary<string, ProgressTask>();

        var result = await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                return await _downloaderService.DownloadBucketAsync(
                    options,
                    progress =>
                    {
                        if (!progressTasks.TryGetValue(progress.Key, out var task))
                        {
                            task = ctx.AddTask($"[cyan]{TruncateKey(progress.Key)}[/]", maxValue: progress.TotalBytes);
                            progressTasks[progress.Key] = task;
                        }

                        if (progress.ErrorMessage != null)
                        {
                            task.Description = $"[red]{TruncateKey(progress.Key)} - FAILED[/]";
                            task.StopTask();
                        }
                        else if (progress.IsComplete)
                        {
                            task.Value = progress.TotalBytes;
                            task.Description = $"[green]{TruncateKey(progress.Key)} - DONE[/]";
                            task.StopTask();
                        }
                        else
                        {
                            task.Value = progress.DownloadedBytes;
                        }
                    },
                    cancellationToken);
            });

        stopwatch.Stop();

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Download Complete[/]").RuleStyle("grey").LeftJustified());
        AnsiConsole.WriteLine();

        var resultTable = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[yellow]Metric[/]")
            .AddColumn("[yellow]Value[/]");

        resultTable.AddRow("Total Files Downloaded", $"[green]{result.SuccessCount}[/]");
        resultTable.AddRow("Failed Downloads", result.FailureCount > 0 ? $"[red]{result.FailureCount}[/]" : $"[green]0[/]");
        resultTable.AddRow("Total Size", $"[cyan]{FormatBytes(result.TotalBytesDownloaded)}[/]");
        resultTable.AddRow("Time Elapsed", $"[cyan]{stopwatch.Elapsed:mm\\:ss}[/]");

        if (result.TotalBytesDownloaded > 0 && stopwatch.Elapsed.TotalSeconds > 0)
        {
            var bytesPerSecond = result.TotalBytesDownloaded / stopwatch.Elapsed.TotalSeconds;
            resultTable.AddRow("Average Speed", $"[cyan]{FormatBytes((long)bytesPerSecond)}/s[/]");
        }

        AnsiConsole.Write(resultTable);

        if (result.Failures.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[red]Failures[/]").RuleStyle("red").LeftJustified());
            AnsiConsole.WriteLine();

            foreach (var (key, error) in result.Failures)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] {key}: [dim]{error}[/]");
            }
        }

        AnsiConsole.WriteLine();

        if (result.IsSuccess)
        {
            AnsiConsole.MarkupLine("[green]✓ All files downloaded successfully![/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]⚠ Download completed with {result.FailureCount} failures.[/]");
        }
    }

    private static string TruncateKey(string key, int maxLength = 50)
    {
        if (key.Length <= maxLength)
        {
            return key;
        }

        var extension = Path.GetExtension(key);
        var truncated = key.Substring(0, maxLength - extension.Length - 3);
        return $"{truncated}...{extension}";
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}
