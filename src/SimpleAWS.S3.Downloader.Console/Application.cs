using System.Collections.ObjectModel;
using System.Diagnostics;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using SimpleAWS.S3.Downloader.Console.Settings;
using SimpleAWS.S3.Downloader.Core.Models;
using SimpleAWS.S3.Downloader.Core.Services;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace SimpleAWS.S3.Downloader.Console;

public sealed class Application
{
    private readonly IS3DownloaderService _downloaderService;
    private readonly AppSettingsStore _settingsStore;
    private readonly SsoTokenReader _ssoTokenReader;
    private readonly Func<AwsSettings, IAmazonS3> _s3Factory;
    private readonly AwsSettings _startupAwsSettings;
    private IApplication? _guiApp;
    private Action<string>? _appendLog;

    public Application(
        IS3DownloaderService downloaderService,
        AppSettingsStore settingsStore,
        Func<AwsSettings, IAmazonS3> s3Factory,
        IConfiguration configuration)
    {
        _downloaderService = downloaderService ?? throw new ArgumentNullException(nameof(downloaderService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _ssoTokenReader = new SsoTokenReader();
        _s3Factory = s3Factory ?? throw new ArgumentNullException(nameof(s3Factory));

        ArgumentNullException.ThrowIfNull(configuration);
        _startupAwsSettings = new AwsSettings
        {
            Profile = configuration["AWS:Profile"],
            Region = configuration["AWS:Region"] ?? configuration["AWS_REGION"]
        };
    }

    public async Task RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        await Task.Yield();

        using var app = Terminal.Gui.App.Application.Create();
        app.Init();
        _guiApp = app;

        var win = new Window
        {
            Title = "S3 Downloader",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        var settingsLabel = new Label
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill() - 2,
            Height = 4
        };

        var menu = new MenuBar
        {
            Menus =
            [
                new MenuBarItem(
                    "_Actions",
                    [
                        new MenuItem("_Settings", "Edit AWS profile/region", ShowSettingsDialog),
                        new MenuItem("_Backup single bucket", "Select bucket and download", () => RunAsyncAction(() => BackupSingleBucketAsync(cancellationToken), cancellationToken)),
                        new MenuItem("Backup _all buckets", "Download all buckets", () => RunAsyncAction(() => BackupAllBucketsAsync(cancellationToken), cancellationToken)),
                    ]),
                new MenuBarItem(
                    "_File",
                    [
                        new MenuItem("_Quit", "Quit", () => app.RequestStop())
                    ])
            ]
        };
        win.Add(menu);

        var settingsFrame = new FrameView
        {
            Title = "Current Settings",
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = 7
        };
        win.Add(settingsFrame);
        settingsFrame.Add(settingsLabel);

        var body = new FrameView
        {
            Title = "Activity Log",
            X = 0,
            Y = Pos.Bottom(settingsFrame),
            Width = Dim.Fill(),
            Height = Dim.Fill() - 1
        };
        win.Add(body);

        var logView = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            WordWrap = true
        };
        body.Add(logView);

        var quitShortcut = new Shortcut
        {
            Title = "Quit",
            Key = Key.Q.WithCtrl
        };
        quitShortcut.Accepting += (_, _) => app.RequestStop();

        var statusBar = new StatusBar
        {
            Visible = true
        };
        statusBar.Add(quitShortcut);
        win.Add(statusBar);

        void AppendLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var line = $"[{timestamp}] {message}\n";
            logView.Text = logView.Text + line;
            logView.MoveEnd();
        }

        _appendLog = AppendLog;

        // Periodically refresh the settings panel to pick up auth changes.
        app.AddTimeout(TimeSpan.FromSeconds(10), () =>
        {
            RefreshSettings();
            return true;
        });

        AppendLog("Ready. Use the Actions menu to start a backup.");
        RefreshSettings();
        app.Run(win);

        void RefreshSettings()
        {
            var effective = GetEffectiveAwsSettings();
            var appSettings = _settingsStore.LoadAppSettings();
            var authResult = _ssoTokenReader.ReadAuthStatus(effective.Profile);
            settingsLabel.Text = $"Profile:  {effective.Profile ?? "(default)"}\n" +
                                 $"Region:   {effective.Region ?? "(from profile)"}\n" +
                                 $"Download: {appSettings.DefaultDownloadPath ?? "(current directory)"}\n" +
                                 $"Auth:     {authResult.StatusText}";
        }

        AwsSettings GetEffectiveAwsSettings()
        {
            var persisted = _settingsStore.LoadAwsSettings();
            return new AwsSettings
            {
                Profile = persisted.Profile ?? _startupAwsSettings.Profile,
                Region = persisted.Region ?? _startupAwsSettings.Region
            };
        }

        void ShowSettingsDialog()
        {
            var effective = GetEffectiveAwsSettings();
            var currentAppSettings = _settingsStore.LoadAppSettings();

            var dialog = new Dialog
            {
                Title = "Settings",
                Width = 70,
                Height = 16
            };

            dialog.Add(new Label { X = 1, Y = 1, Text = "AWS profile:" });
            var profileField = new TextField { X = 18, Y = 1, Width = 47, Text = effective.Profile ?? string.Empty };
            dialog.Add(profileField);

            dialog.Add(new Label { X = 1, Y = 3, Text = "Region override:" });
            var regionField = new TextField { X = 18, Y = 3, Width = 47, Text = effective.Region ?? string.Empty };
            dialog.Add(regionField);

            dialog.Add(new Label { X = 1, Y = 5, Text = "Download folder:" });
            var downloadPathField = new TextField { X = 18, Y = 5, Width = 47, Text = currentAppSettings.DefaultDownloadPath ?? string.Empty };
            dialog.Add(downloadPathField);

            dialog.Add(new Label { X = 1, Y = 8, Text = "Clear a field to save null." });

            var cancelBtn = new Button { Text = "Cancel" };
            cancelBtn.Accepting += (_, _) => app.RequestStop();

            var saveBtn = new Button { Text = "Save", IsDefault = true };
            saveBtn.Accepting += (_, _) =>
            {
                var profileText = profileField.Text;
                var regionText = regionField.Text;
                var downloadPathText = downloadPathField.Text;

                var updatedAws = new AwsSettings
                {
                    Profile = string.IsNullOrWhiteSpace(profileText) ? null : profileText,
                    Region = string.IsNullOrWhiteSpace(regionText) ? null : regionText
                };

                var updatedApp = new AppSettings
                {
                    DefaultDownloadPath = string.IsNullOrWhiteSpace(downloadPathText) ? null : downloadPathText
                };

                _settingsStore.SaveAwsSettings(updatedAws);
                _settingsStore.SaveAppSettings(updatedApp);
                RefreshSettings();
                app.RequestStop();
            };

            dialog.AddButton(cancelBtn);
            dialog.AddButton(saveBtn);

            app.Run(dialog);
        }
    }

    private void RunAsyncAction(Func<Task> action, CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex) when (IsSsoTokenExpiredException(ex))
            {
                _guiApp!.Invoke(HandleSsoTokenExpired);
            }
            catch (Exception ex)
            {
                _guiApp!.Invoke(() => MessageBox.ErrorQuery(_guiApp!, "Error", ex.Message, "OK"));
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Appends a log message to the activity log on the UI thread.
    /// </summary>
    private void Log(string message)
    {
        _guiApp?.Invoke(() => _appendLog?.Invoke(message));
    }

    /// <summary>
    /// Invokes an action on the UI thread and waits for it to complete.
    /// Use this to run a dialog and reliably read values set by the dialog after it closes.
    /// </summary>
    private Task InvokeAndAwaitAsync(Action action)
    {
        var done = new TaskCompletionSource();
        _guiApp!.Invoke(() =>
        {
            try
            {
                action();
            }
            finally
            {
                done.SetResult();
            }
        });
        return done.Task;
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):F1} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):F1} KB",
        _ => $"{bytes:N0} B"
    };

    private static string FormatElapsed(TimeSpan elapsed) => elapsed.TotalHours >= 1
        ? elapsed.ToString(@"h\:mm\:ss")
        : elapsed.ToString(@"m\:ss");

    private static string TruncatePath(string? path, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length <= maxLength)
        {
            return path ?? string.Empty;
        }

        return "..." + path[^(maxLength - 3)..];
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

    /// <summary>
    /// Gets the currently resolved AWS profile name from persisted or startup settings.
    /// </summary>
    private string? GetCurrentProfile()
    {
        var persisted = _settingsStore.LoadAwsSettings();
        return persisted.Profile ?? _startupAwsSettings.Profile;
    }

    /// <summary>
    /// Gets the configured default download path, falling back to the current working directory.
    /// </summary>
    private string GetDefaultDownloadPath()
    {
        var appSettings = _settingsStore.LoadAppSettings();
        return !string.IsNullOrWhiteSpace(appSettings.DefaultDownloadPath)
            ? appSettings.DefaultDownloadPath
            : Environment.CurrentDirectory;
    }

    /// <summary>
    /// Determines whether the exception (or any inner exception) indicates an expired AWS SSO token.
    /// </summary>
    private static bool IsSsoTokenExpiredException(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is AmazonServiceException { ErrorCode: "ExpiredToken" or "ExpiredTokenException" })
            {
                return true;
            }

            var message = current.Message;
            if (!string.IsNullOrWhiteSpace(message) &&
                message.Contains("SSO", StringComparison.OrdinalIgnoreCase) &&
                (message.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("invalid", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to open a new terminal window running <c>aws sso login</c> for the given profile.
    /// </summary>
    /// <returns><see langword="true"/> if the process was started; otherwise <see langword="false"/>.</returns>
    private static bool TryOpenSsoLogin(string profile)
    {
        try
        {
            ProcessStartInfo psi;

            if (OperatingSystem.IsWindows())
            {
                psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c start \"AWS SSO Login\" cmd /k \"aws sso login --profile {profile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }
            else if (OperatingSystem.IsMacOS())
            {
                psi = new ProcessStartInfo
                {
                    FileName = "osascript",
                    Arguments = $"-e 'tell application \"Terminal\" to do script \"aws sso login --profile {profile}\"'",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }
            else
            {
                psi = new ProcessStartInfo
                {
                    FileName = "aws",
                    Arguments = $"sso login --profile {profile}",
                    UseShellExecute = true
                };
            }

            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Shows a dialog informing the user about SSO expiration and offers to open a login window.
    /// </summary>
    private void HandleSsoTokenExpired()
    {
        var profile = GetCurrentProfile();

        if (string.IsNullOrWhiteSpace(profile))
        {
            MessageBox.ErrorQuery(
                _guiApp!,
                "SSO Error",
                "Your AWS session has expired, but no profile is configured.\nSet a profile in Settings first.",
                "OK");
            return;
        }

        var choice = MessageBox.Query(
            _guiApp!,
            "SSO Session Expired",
            $"Your AWS SSO session for profile '{profile}' has expired.\n\n" +
            "Would you like to open a new window to refresh your SSO login?",
            "Open Login",
            "Cancel");

        if (choice != 0)
        {
            return;
        }

        if (TryOpenSsoLogin(profile))
        {
            MessageBox.Query(
                _guiApp!,
                "SSO Login",
                "A login window has been opened.\n\n" +
                "Complete the SSO login in the browser,\nthen retry your action from the Actions menu.",
                "OK");
        }
        else
        {
            MessageBox.ErrorQuery(
                _guiApp!,
                "Error",
                $"Could not open SSO login automatically.\n\nPlease run manually in a terminal:\n  aws sso login --profile {profile}",
                "OK");
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

    private async Task BackupSingleBucketAsync(CancellationToken cancellationToken)
    {
        Log("Listing buckets...");
        var buckets = await ListBucketsAsync(cancellationToken).ConfigureAwait(false);
        Log($"Found {buckets.Count} bucket(s).");

        if (buckets.Count == 0)
        {
            await InvokeAndAwaitAsync(() =>
                MessageBox.Query(_guiApp!, "No buckets", "No buckets found.", "OK")).ConfigureAwait(false);
            return;
        }

        string? selectedBucket = null;
        string? prefix = null;
        var defaultPath = GetDefaultDownloadPath();
        string localPath = defaultPath;
        var confirmed = false;

        await InvokeAndAwaitAsync(() =>
        {
            var dialog = new Dialog
            {
                Title = "Backup single bucket",
                Width = 70,
                Height = 20
            };

            dialog.Add(new Label { X = 1, Y = 1, Text = "Bucket:" });
            var list = new ListView { X = 1, Y = 2, Width = Dim.Fill() - 2, Height = 8 };
            list.SetSource(new ObservableCollection<string>(buckets));
            dialog.Add(list);

            dialog.Add(new Label { X = 1, Y = 11, Text = "Prefix (optional):" });
            var prefixField = new TextField { X = 18, Y = 11, Width = Dim.Fill() - 20 };
            dialog.Add(prefixField);

            dialog.Add(new Label { X = 1, Y = 13, Text = "Local path:" });
            var pathField = new TextField { X = 18, Y = 13, Width = Dim.Fill() - 20, Text = defaultPath };
            dialog.Add(pathField);

            var cancelBtn = new Button { Text = "Cancel" };
            cancelBtn.Accepting += (_, _) => _guiApp!.RequestStop();

            var startBtn = new Button { Text = "Start", IsDefault = true };
            startBtn.Accepting += (_, _) =>
            {
                selectedBucket = list.SelectedItem is int idx ? buckets[idx] : null;
                prefix = prefixField.Text;
                localPath = string.IsNullOrWhiteSpace(pathField.Text) ? defaultPath : pathField.Text;
                confirmed = true;
                _guiApp!.RequestStop();
            };

            dialog.AddButton(cancelBtn);
            dialog.AddButton(startBtn);

            _guiApp!.Run(dialog);
        }).ConfigureAwait(false);

        if (!confirmed || string.IsNullOrWhiteSpace(selectedBucket))
        {
            Log("Single-bucket backup cancelled by user.");
            return;
        }

        var resolvedPrefix = string.IsNullOrWhiteSpace(prefix) ? null : prefix;

        Log($"Scanning bucket '{selectedBucket}'...");
        var summary = await _downloaderService.GetBucketSummaryAsync(
            selectedBucket, resolvedPrefix, cancellationToken).ConfigureAwait(false);
        Log($"Bucket '{selectedBucket}': {summary.ObjectCount:N0} objects, {FormatBytes(summary.TotalSizeBytes)}.");

        if (summary.ObjectCount == 0)
        {
            Log($"Bucket '{selectedBucket}' is empty, skipping.");
            await InvokeAndAwaitAsync(() =>
                MessageBox.Query(_guiApp!, "Empty bucket", "No downloadable objects found.", "OK")).ConfigureAwait(false);
            return;
        }

        var options = new DownloadOptions
        {
            BucketName = selectedBucket,
            Prefix = resolvedPrefix,
            LocalPath = Path.Combine(localPath, selectedBucket),
            Region = null,
            OverwriteExisting = true,
            MaxConcurrency = 5
        };

        Log($"Starting download of '{selectedBucket}' to '{options.LocalPath}'.");
        await RunDownloadAsync(options, summary, cancellationToken).ConfigureAwait(false);
    }

    private async Task BackupAllBucketsAsync(CancellationToken cancellationToken)
    {
        Log("Listing all buckets...");
        var buckets = await ListBucketsAsync(cancellationToken).ConfigureAwait(false);
        Log($"Found {buckets.Count} bucket(s).");

        if (buckets.Count == 0)
        {
            await InvokeAndAwaitAsync(() =>
                MessageBox.Query(_guiApp!, "No buckets", "No buckets found.", "OK")).ConfigureAwait(false);
            return;
        }

        var defaultPath = GetDefaultDownloadPath();
        var basePath = defaultPath;
        var confirmed = false;

        await InvokeAndAwaitAsync(() =>
        {
            var dialog = new Dialog
            {
                Title = "Backup all buckets",
                Width = 70,
                Height = 12
            };

            dialog.Add(new Label { X = 1, Y = 1, Text = "Base local path:" });
            var pathField = new TextField { X = 18, Y = 1, Width = Dim.Fill() - 20, Text = defaultPath };
            dialog.Add(pathField);
            dialog.Add(new Label { X = 1, Y = 3, Text = $"Buckets: {buckets.Count}" });

            var cancelBtn = new Button { Text = "Cancel" };
            cancelBtn.Accepting += (_, _) => _guiApp!.RequestStop();

            var startBtn = new Button { Text = "Start", IsDefault = true };
            startBtn.Accepting += (_, _) =>
            {
                basePath = string.IsNullOrWhiteSpace(pathField.Text) ? defaultPath : pathField.Text;
                confirmed = true;
                _guiApp!.RequestStop();
            };

            dialog.AddButton(cancelBtn);
            dialog.AddButton(startBtn);

            _guiApp!.Run(dialog);
        }).ConfigureAwait(false);

        if (!confirmed)
        {
            Log("All-buckets backup cancelled by user.");
            return;
        }

        Log($"Starting backup of {buckets.Count} bucket(s) to '{basePath}'.");

        for (var i = 0; i < buckets.Count; i++)
        {
            var bucket = buckets[i];
            cancellationToken.ThrowIfCancellationRequested();

            Log($"Scanning bucket '{bucket}' ({i + 1}/{buckets.Count})...");
            var summary = await _downloaderService
                .GetBucketSummaryAsync(bucket, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (summary.ObjectCount == 0)
            {
                Log($"Bucket '{bucket}' is empty, skipping.");
                continue;
            }

            Log($"Bucket '{bucket}': {summary.ObjectCount:N0} objects, {FormatBytes(summary.TotalSizeBytes)}.");

            var options = new DownloadOptions
            {
                BucketName = bucket,
                Prefix = null,
                LocalPath = Path.Combine(basePath, bucket),
                Region = null,
                OverwriteExisting = true,
                MaxConcurrency = 5
            };

            Log($"Downloading '{bucket}' to '{options.LocalPath}'.");
            var cancelled = await RunDownloadAsync(options, summary, cancellationToken).ConfigureAwait(false);

            if (cancelled)
            {
                Log("Download cancelled by user, stopping remaining buckets.");
                break;
            }
        }

        Log("All-buckets backup complete.");
    }

    /// <summary>
    /// Runs the download and shows a progress dialog.
    /// Returns <see langword="true"/> if the user cancelled by closing the dialog.
    /// </summary>
    private async Task<bool> RunDownloadAsync(DownloadOptions options, BucketSummary summary, CancellationToken cancellationToken)
    {
        var totalFiles = summary.ObjectCount;
        var totalBytes = summary.TotalSizeBytes;
        var completedFiles = 0;
        var downloadedBytes = 0L;
        var errorCount = 0;
        var currentFile = string.Empty;
        DownloadResult? downloadResult = null;
        Exception? downloadError = null;
        var isFinished = false;
        var stopwatch = Stopwatch.StartNew();

        // Linked CTS so closing the dialog cancels the download.
        using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Start the download before showing the dialog so progress is
        // driven by a timer that polls shared state instead of relying
        // on _guiApp.Invoke from background threads, which is not
        // reliably dispatched during a nested Application.Run call.
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _downloaderService
                    .DownloadBucketAsync(options, OnProgress, downloadCts.Token)
                    .ConfigureAwait(false);

                downloadResult = result;
            }
            catch (OperationCanceledException)
            {
                // Download was cancelled by closing the dialog.
            }
            catch (Exception ex)
            {
                downloadError = ex;
            }
            finally
            {
                Volatile.Write(ref isFinished, true);
            }
        });

        await InvokeAndAwaitAsync(() =>
        {
            var dialog = new Dialog
            {
                Title = $"Downloading: {options.BucketName}",
                Width = 70,
                Height = 14
            };

            var summaryLabel = new Label
            {
                X = 1, Y = 1, Width = Dim.Fill() - 2,
                Text = $"Objects: {totalFiles:N0}   Total size: {FormatBytes(totalBytes)}"
            };
            dialog.Add(summaryLabel);

            var progressBar = new ProgressBar
            {
                X = 1, Y = 3, Width = Dim.Fill() - 2,
                Fraction = 0f
            };
            dialog.Add(progressBar);

            var statsLabel = new Label
            {
                X = 1, Y = 5, Width = Dim.Fill() - 2, Height = 4,
                Text = $"Progress:   0% (0 / {totalFiles:N0} files)\n" +
                       $"Downloaded: 0 B / {FormatBytes(totalBytes)}\n" +
                       "Current:    Starting...\n" +
                       "Errors:     0"
            };
            dialog.Add(statsLabel);

            var canClose = false;
            var actionBtn = new Button { Text = "Cancel", IsDefault = true };
            actionBtn.Accepting += (_, _) =>
            {
                if (canClose)
                {
                    _guiApp!.RequestStop();
                }
                else if (!downloadCts.IsCancellationRequested)
                {
                    downloadCts.Cancel();
                    actionBtn.Text = "Cancelling...";
                }
            };
            dialog.AddButton(actionBtn);

            // Block Escape from closing the dialog while the download is active.
            dialog.KeyDown += (_, e) =>
            {
                if (!canClose && e == Key.Esc)
                {
                    e.Handled = true;
                }
            };

            // Poll progress on a timer instead of using Invoke from background threads.
            _guiApp!.AddTimeout(TimeSpan.FromMilliseconds(250), () =>
            {
                if (Volatile.Read(ref isFinished))
                {
                    stopwatch.Stop();
                    canClose = true;
                    actionBtn.Text = "Close";

                    if (downloadError is not null && IsSsoTokenExpiredException(downloadError))
                    {
                        _guiApp!.RequestStop();
                        HandleSsoTokenExpired();
                        return false;
                    }

                    if (downloadError is not null)
                    {
                        _appendLog?.Invoke($"'{options.BucketName}' failed: {downloadError.Message}");
                        _guiApp!.RequestStop();
                        return false;
                    }

                    if (downloadResult is not null)
                    {
                        var elapsed = stopwatch.Elapsed;
                        var speed = elapsed.TotalSeconds > 0
                            ? FormatBytes((long)(downloadResult.TotalBytesDownloaded / elapsed.TotalSeconds)) + "/s"
                            : "--";

                        progressBar.Fraction = 1f;
                        statsLabel.Text =
                            $"Complete! {downloadResult.SuccessCount:N0} files in {FormatElapsed(elapsed)}\n" +
                            $"Downloaded: {FormatBytes(downloadResult.TotalBytesDownloaded)} @ {speed}\n" +
                            $"Failures:   {downloadResult.FailureCount:N0}\n" +
                            " ";

                        _appendLog?.Invoke(
                            $"'{options.BucketName}' complete: {downloadResult.SuccessCount:N0} files, " +
                            $"{FormatBytes(downloadResult.TotalBytesDownloaded)} in {FormatElapsed(elapsed)} (avg {speed})" +
                            (downloadResult.FailureCount > 0 ? $", {downloadResult.FailureCount:N0} failed" : string.Empty) +
                            ".");
                    }
                    else
                    {
                        _appendLog?.Invoke($"'{options.BucketName}' download cancelled.");
                    }

                    _guiApp!.RequestStop();
                    return false;
                }

                var completed = Volatile.Read(ref completedFiles);
                var bytes = Interlocked.Read(ref downloadedBytes);
                var errors = Volatile.Read(ref errorCount);
                var file = downloadCts.IsCancellationRequested
                    ? "Cancelling..."
                    : TruncatePath(Volatile.Read(ref currentFile), 50);
                var fraction = totalFiles > 0 ? (float)completed / totalFiles : 0f;
                var percent = (int)(fraction * 100);

                progressBar.Fraction = fraction;
                statsLabel.Text =
                    $"Progress:   {percent}% ({completed:N0} / {totalFiles:N0} files)\n" +
                    $"Downloaded: {FormatBytes(bytes)} / {FormatBytes(totalBytes)}\n" +
                    $"Current:    {file}\n" +
                    $"Errors:     {errors:N0}";

                return true;
            });

            _guiApp!.Run(dialog);
        }).ConfigureAwait(false);

        return downloadCts.IsCancellationRequested;

        void OnProgress(DownloadProgress progress)
        {
            if (progress.IsComplete)
            {
                Interlocked.Increment(ref completedFiles);
                Interlocked.Add(ref downloadedBytes, progress.TotalBytes);
            }
            else if (progress.ErrorMessage is not null)
            {
                Interlocked.Increment(ref errorCount);
                Interlocked.Increment(ref completedFiles);
            }
            else
            {
                Volatile.Write(ref currentFile, progress.Key);
            }
        }
    }
}
