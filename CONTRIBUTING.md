# Contributing to WinParted

Thanks for helping. This page is for people who build, test or release WinParted. For what the app does and how to get it, see the [README](README.md).

## Building

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Visual Studio 2026 (with the "WinUI application development" workload) is optional but recommended for working on the UI.

```bash
dotnet build
```

Run the unit tests:

```bash
dotnet test tests/WinParted.Core.Tests
```

`tests/WinParted.Storage.IntegrationTests` create and modify temporary virtual disks (VHDX), never your real disks, and need an elevated terminal (Terminal (Admin)).

## Running

Run the app from an **elevated** terminal to see your real disks:

```bash
dotnet run --project src/WinParted.App
```

Release builds always ask for administrator rights. Debug builds start without them and then show the "needs administrator rights" screen, which makes that screen and sample mode easy to try.

**Sample mode** shows the design's example disks and never changes anything:

```bash
dotnet run --project src/WinParted.App -- --sample
```

Developer options:

| Option | Effect |
|---|---|
| `--theme dark\|light` | Force a theme |
| `--culture <name>` | Number formatting, e.g. `en-US` |
| `--select <disk>[:<segment>]` | Select a disk (and segment) after loading, e.g. `--select 0:2` |
| `--open <action>` | Open an action's dialog after loading, e.g. `--open ClearDisk` |
| `--pretend-version <x.y.z>` | The update check compares GitHub's releases with this version, and runs even without administrator rights |

## Files at runtime

WinParted writes only to `%ProgramData%\WinParted`: `settings.json` and the diagnostic log `winparted.log` (the `WINPARTED_LOG` environment variable overrides the log path). The uninstaller removes that folder, and also `%LOCALAPPDATA%\WinParted`, where 0.1.0 kept its log.

## Releasing

The version is set in one place: `<Version>` in `Directory.Build.props` (About, the update check, the zip and the installer all use it). The release bundles the .NET runtime from the SDK that builds it, so use an up-to-date SDK.

CI builds both release files on every push to `main`: the published folder (artifact `WinParted-win-x64`) and the installer (artifact `WinParted-setup`). The simplest release takes them from the CI run of the commit being released:

```bash
gh run download <run-id> -n WinParted-setup -D artifacts/release
gh run download <run-id> -n WinParted-win-x64 -D artifacts/release/WinParted-win-x64
```

Zip the `WinParted-win-x64` folder as `WinParted-<version>-win-x64.zip`, attach it and `WinParted-<version>-setup.exe` to a draft GitHub release with tag `v<version>`, then publish it. Only full releases (not pre-releases) trigger the update notice for users on default settings.

To build locally instead:

```bash
dotnet publish src/WinParted.App -c Release -r win-x64 -o artifacts/WinParted-win-x64
```

Then build the installer with [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`ISCC.exe` on `PATH`), from the repository root:

```powershell
$version = (dotnet msbuild src/WinParted.App/WinParted.App.csproj -getProperty:Version).Trim()
iscc "/DAppVersion=$version" installer\WinParted.iss
```

This creates `artifacts/WinParted-<version>-setup.exe`.

The app icon is built from the SVGs in `docs/design/app-icon/` by `make-icon.ps1` (needs ImageMagick).

## Project layout

| Path | Contents |
|---|---|
| `src/WinParted.App` | WinUI 3 user interface |
| `src/WinParted.Core` | Disk model, safety rules and action availability (no Windows APIs, fully unit tested) |
| `src/WinParted.Storage` | Access to the Windows Storage Management API |
| `tests/` | Unit and integration tests |
| `installer/` | Inno Setup script for the installer |
| `docs/design/` | UI design briefs and the app icon sources |
| `openspec/` | Specifications (`specs/`) and planned or archived changes (`changes/`) |

New features start as an OpenSpec change: proposal, specs, design and tasks, reviewed before any code is written.
