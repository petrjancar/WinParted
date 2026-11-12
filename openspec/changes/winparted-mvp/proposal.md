# Proposal

## Why

Windows users who need to manage disks and partitions have two choices: the built-in Disk Management console, which is dated, terse and often unclear about why an action is unavailable, or third-party tools that are closed-source or upsell-heavy. GParted fills this role on Linux but does not run natively on Windows. WinParted is an open-source (MIT), modern, easy-to-understand Windows 11 disk manager built on the Windows Storage Management API (SMP). This change delivers its MVP.

## What Changes

- New WinUI 3 desktop application **WinParted** (C#, Windows 11 only, MIT license). Windows 10 is not supported.
- Read-only overview of all disks: list with health and bus type, graphical partition bar per disk, details panel, used/free space per volume, manual refresh, and automatic refresh when disks are connected or disconnected.
- Disk operations: initialize (GPT or MBR), bring online/offline, clear disk (removes all partitions; confirmed by typing the disk number).
- Partition/volume operations: create partition (with optional format), delete, format, shrink and extend (NTFS/ReFS only), change drive letter, change volume label.
- Operations run **immediately**: validate, confirm, execute, refresh. There is no pending-operations queue.
- Safety model: system-critical items (EFI, MSR, Recovery, the Windows volume, pagefile volumes, locked BitLocker volumes, dynamic disks, Storage Spaces members, the system disk) stay visible but are **locked** against destructive actions, and the app explains why.
- Every unavailable action states the reason in plain language (for example, "C: can only grow into free space directly after it. The Recovery partition is in the way").
- A features document for Claude Design, describing the MVP screens, states and dialogs, so the UI design can be produced before or alongside implementation.

Out of scope (v2+): turning off a disk's read-only flag (planned for the next version; the MVP only lets an offline read-only disk be brought online), check & repair, moving or copying partitions, queued operations, non-destructive MBR/GPT conversion, Linux filesystems, Storage Spaces management, dynamic disk management. Planned for the next version as well: an installer and an in-app update notice (see design, Distribution and updates).

## Capabilities

### New Capabilities
- `disk-inventory`: Discovering disks, partitions and volumes and presenting them (list, partition bar, details, space usage, manual refresh).
- `device-change-detection`: Reacting to disks being connected, disconnected or changed while the app runs.
- `operation-execution`: The common lifecycle every modifying operation follows: availability with reasons, confirmation, target re-verification, one-at-a-time execution, progress, and error reporting.
- `protected-storage`: Classifying system-critical disks, partitions and volumes and locking destructive actions on them.
- `disk-operations`: Initialize, online/offline and clear disk.
- `partition-operations`: Create, delete, format, shrink/extend, change drive letter and change label.
- `app-privileges`: Running with the administrator rights SMP modifications need.

### Modified Capabilities

None (greenfield project).

## Impact

- New repository content: Visual Studio solution, WinUI 3 app project, storage service library wrapping SMP, unit and integration test projects, README, LICENSE (MIT), and the Claude Design features document.
- Dependencies: .NET (current LTS), Windows App SDK (WinUI 3), Microsoft.Management.Infrastructure (CIM), CommunityToolkit.Mvvm.
- System impact: the app modifies real disks with administrator rights; integration tests must run only against temporary VHDX files.
