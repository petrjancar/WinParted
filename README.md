# WinParted

[![CI](https://github.com/petrjancar/WinParted/actions/workflows/ci.yml/badge.svg)](https://github.com/petrjancar/WinParted/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Support on Ko-fi](https://img.shields.io/badge/Ko--fi-support-FF5E5B?logo=ko-fi&logoColor=white)](https://ko-fi.com/petrjancar)

A free, open-source disk and partition manager for Windows 11 with a modern, native UI.

> **Status:** early development. WinParted changes real disks, so back up anything important first.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/screenshot-dark.png">
  <img alt="WinParted showing a disk's partition layout, the details of the selected volume and its available actions (sample data)" src="docs/images/screenshot-light.png">
</picture>

## Features

- **See your disks:** health, bus type and partition style of every disk, a visual partition map, and used and free space per volume. Updates when you plug in or remove a drive.
- **Disk operations:** initialize (GPT or MBR), bring online or take offline, clear disk, turn off read-only.
- **Partition operations:** create (and format), delete, format, shrink and extend, change drive letter and label.
- **Safe by design:** system partitions (EFI, Recovery, the Windows volume and others) are shown but locked, every unavailable action tells you why, and destructive changes ask for confirmation first.

## Download

Get the latest version from [Releases](https://github.com/petrjancar/WinParted/releases): the **installer** (`WinParted-<version>-setup.exe`) or a **portable zip**. Requires Windows 11 (x64); nothing else to install.

WinParted isn't code-signed yet, so Windows SmartScreen may warn about an unknown publisher. Choose **More info → Run anyway**.

WinParted tells you when a new version is available. The check sends nothing about your PC or disks, and you can turn it off in **About**.

## Limitations

WinParted can do what Windows itself supports, and no more:

- **Partitions can't be moved.** A volume can only grow into free space directly after it. (This is why "Extend" is often unavailable for C: when a Recovery partition follows it.)
- **Only NTFS and ReFS volumes can be resized.**
- Linux file systems (ext4, btrfs, ...) are shown as "Unknown file system" and can only be deleted.
- Dynamic disks and Storage Spaces are shown but can't be changed.

## Contributing

Building, testing and releasing are described in [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE)
