# WinParted

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Support on Ko-fi](https://img.shields.io/badge/Ko--fi-support-FF5E5B?logo=ko-fi&logoColor=white)](https://ko-fi.com/petrjancar)

A free, open-source disk and partition manager for Windows 11, with a modern UI that explains what it's doing. Think GParted, but native to Windows and built for people who aren't storage experts.

> **Status:** early development. Use at your own risk.

## What it does (MVP)

- **See your disks:** every disk with its health, bus type and partition style, a visual partition map, and used/free space per volume. Updates automatically when you plug in or remove a drive.
- **Disk operations:** initialize (GPT or MBR), bring online / take offline, clear disk.
- **Partition operations:** create (and format), delete, format, shrink and extend, change drive letter, change label.
- **Safety first:** system partitions (EFI, Recovery, the Windows volume and others) are always shown but locked against destructive changes, and every unavailable action tells you why.

Operations run immediately after you confirm them. There is no pending queue.

## Limitations

WinParted uses the [Windows Storage Management API](https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/windows-storage-management-api-portal), so it can do what Windows itself supports and no more:

- **Partitions can't be moved.** A volume can only grow into free space directly after it. (This is why "Extend" is often unavailable for C: when a Recovery partition follows it.)
- **Only NTFS and ReFS volumes can be resized.**
- Linux filesystems (ext4, btrfs, ...) are shown as "Unknown filesystem" and can only be deleted.
- Dynamic disks and Storage Spaces are shown but can't be changed.

## Requirements

- **Windows 11**.
- Administrator rights: WinParted asks for them when it starts.

## Building

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Visual Studio 2026 (with the "WinUI application development" workload) is optional but recommended for working on the UI.

```bash
dotnet build
```

Run the unit tests:

```bash
dotnet test tests/WinParted.Core.Tests
```

Run the app from an **elevated** terminal (Terminal (Admin)) to see your real disks:

```bash
dotnet run --project src/WinParted.App
```

Release builds always ask for administrator rights. Debug builds start without them and then show
the "needs administrator rights" screen, which makes that screen and sample mode easy to try.

**Sample mode** shows the design's example disks and never changes anything:

```bash
dotnet run --project src/WinParted.App -- --sample
```

Developer options: `--theme dark|light`, `--select <disk>[:<segment>]` (e.g. `--select 0:2`) and `--open <action>` (e.g. `--open ClearDisk`).

Publish a self-contained release build (no .NET or Windows App SDK needed on the target PC):

```bash
dotnet publish src/WinParted.App -c Release -r win-x64 -o artifacts/WinParted-win-x64
```

Run `WinParted.exe` from that folder; Windows asks for administrator rights.

Releases are published as this folder zipped (`WinParted-<version>-win-x64.zip`) on GitHub Releases. To update, download the new zip and replace the folder.

`tests/WinParted.Storage.IntegrationTests` create and modify temporary virtual disks (VHDX), never your real disks, and need an elevated terminal.

## Project layout

| Path | Contents |
|---|---|
| `src/WinParted.App` | WinUI 3 user interface |
| `src/WinParted.Core` | Disk model, safety rules and action availability (no Windows APIs, fully unit tested) |
| `src/WinParted.Storage` | Access to the Windows Storage Management API |
| `tests/` | Unit and integration tests |
| `docs/design/` | UI design brief |
| `openspec/` | Specifications and planned changes |

## License

[MIT](LICENSE)
