# SimpleAWS.S3.Downloader

Interactive console app for downloading objects from an AWS S3 bucket to a local folder.

The core downloading logic lives in the `SimpleAWS.S3.Downloader.Core` library and is consumed by the `SimpleAWS.S3.Downloader.Console` UI.

## Tech specs

- .NET: `.NET 10`
- AWS SDK: `AWSSDK.S3`
- Console UI: `Spectre.Console`
- Logging: `Microsoft.Extensions.Logging.Console`
- Testing: xUnit v3 (`xunit.v3.mtp-v2`) on Microsoft Testing Platform
- Package management: Central Package Management (`Directory.Packages.props`)

## Repo layout

- `src/SimpleAWS.S3.Downloader.Core`
  - Library containing `IS3DownloaderService` and `S3DownloaderService`.
  - Uses `IAmazonS3` via DI for testability (no direct AWS client creation inside methods).
- `src/SimpleAWS.S3.Downloader.Console`
  - Interactive console application.
  - Builds a DI container and wires up the core service + AWS client.
- `src/SimpleAWS.S3.Downloader.Core.Tests`
  - Unit tests.
  - Run via `dotnet run` (see below).

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
    "Profile": null
  }
}
```

Set `AWS:Profile` to a named profile (for example `master`) to override the default SDK credential chain.

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
