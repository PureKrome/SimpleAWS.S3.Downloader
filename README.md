# SimpleAWS.S3.Downloader

Interactive console app for downloading objects from an AWS S3 bucket to a local folder.

The core downloading logic lives in the `SimpleAWS.S3.Downloader.Core` library and is consumed by the `SimpleAWS.S3.Downloader.Console` UI.

<p align="center">

    <img alt="App-Pic-1" src="https://github.com/user-attachments/assets/e7cec7e1-c36a-4287-9698-75e13989e578" /><br/><br/>

    <img alt="App-Pic-2" src="https://github.com/user-attachments/assets/bb1292af-4825-4e3c-87e5-bba1065f178f" /><br/><br/>

    <img alt="App-Pic-3" src="https://github.com/user-attachments/assets/dd33101a-1cb8-4fb6-a4a2-1491239146c7" /><br/><br/>
</p>

## Tech specs

- .NET: `.NET 10`
- AWS SDK: `AWSSDK.S3`
- Console UI: `Spectre.Console`
- Logging: `Microsoft.Extensions.Logging.Console`
- Testing: xUnit v3 (`xunit.v3.mtp-v2`) on Microsoft Testing Platform
- Package management: Central Package Management (`Directory.Packages.props`)

## How it generally works

1. The console app prompts you for:
   - bucket name
   - optional prefix filter
   - local download directory
   - optional region
   - overwrite behavior
   - max concurrency
2. `S3DownloaderService`:
   - lists objects via `ListObjectsV2`
   - skips “directory marker” keys (keys ending in `/`)
   - downloads each object via `GetObjectAsync`
   - streams the response stream to a local file path
   - limits parallel downloads using a semaphore (`MaxConcurrency`)
   - reports per-object progress via callback so the console UI can render progress bars

## Configuration and AWS profile selection

The console app creates the S3 client using this precedence order:

1. Default AWS SDK behavior (no explicit profile)
2. `appsettings.json` value `AWS:Profile`
3. CLI argument `--profile <name>` / `--profile=<name>`

If a profile name is specified (via config or CLI), credentials are resolved from the standard AWS shared config/credentials files (e.g. `%UserProfile%\\.aws\\config` on Windows).

### `appsettings.json`

File: `src/SimpleAWS.S3.Downloader.Console/appsettings.json`

```jsonc
{
  "AWS": {
    "Profile": null,
    "Region": null
  },
  "App": {
    "DefaultDownloadPath": null
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "System": "Warning"
    }
  }
}
```

Available settings:

- `AWS:Profile`: Named AWS profile to use from the shared config/credentials files. When null, the SDK default chain is used.
- `AWS:Region`: AWS region override. When null, the region is taken from the selected profile.
- `App:DefaultDownloadPath`: Default local download folder shown in the UI. When null, the current working directory is used.
- `Logging:LogLevel:Default`, `Logging:LogLevel:Microsoft`, `Logging:LogLevel:System`: Standard `Microsoft.Extensions.Logging` filter settings.

## Command line arguments

The app is primarily interactive. Supported args:

- `--profile <name>` or `--profile=<name>`
  - Overrides `AWS:Profile` from `appsettings.json`.
  - Forces the app to use the named shared-config profile.

When using `dotnet run`, pass args after `--`:

```powershell
dotnet run --project src\\SimpleAWS.S3.Downloader.Console -- --profile master
```

## Run the console app

```powershell
dotnet run --project src\\SimpleAWS.S3.Downloader.Console
```

## Run tests

This repo uses xUnit v3 + Microsoft Testing Platform on .NET 10.

Run the test project (instead of `dotnet test`):

```powershell
dotnet run --project src\\SimpleAWS.S3.Downloader.Core.Tests\\SimpleAWS.S3.Downloader.Core.Tests.csproj -c Release
```
