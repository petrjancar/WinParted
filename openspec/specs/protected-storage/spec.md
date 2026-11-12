# protected-storage Specification

## Purpose

Prevents users from making the computer unbootable or destroying data that Windows manages, by keeping system-critical items visible but locked against destructive actions, with an explanation.

## Requirements

### Requirement: Protected items remain visible
Protected disks, partitions and volumes SHALL always be shown in the disk list and layout bar, marked with a lock indicator and a one-line plain-language description of their role.

#### Scenario: EFI partition shown
- **WHEN** the user views the system disk
- **THEN** the EFI partition is shown with a lock indicator and the description "EFI System - Windows needs this to start"

### Requirement: System partitions fully locked
EFI System, Microsoft Reserved (MSR) and Recovery partitions, and any partition Windows reports as a system partition (the partition Windows starts from, such as "System Reserved"), SHALL have every modifying operation disabled. The partition holding the Windows installation is covered by "Windows volume restrictions" instead.

#### Scenario: Delete Recovery partition
- **WHEN** the user selects the Recovery partition
- **THEN** Delete, Format, Shrink, Extend, Change letter and Change label are all disabled with the reason that Windows uses this partition to repair itself

### Requirement: Windows volume restrictions
The volume containing the running Windows installation SHALL have Delete, Format and Change drive letter disabled; Shrink, Extend and Change label SHALL remain available subject to their normal conditions.

#### Scenario: Shrink C:
- **WHEN** the user selects the Windows volume C:
- **THEN** Shrink is available and Delete and Format are disabled with the reason "Windows is running from this volume"

### Requirement: Pagefile and crash-dump volume restrictions
A volume holding an active pagefile or crash-dump file SHALL have Delete, Format and Change drive letter disabled.

#### Scenario: Pagefile on D:
- **WHEN** Windows keeps its pagefile on D:
- **THEN** Delete and Format on D: are disabled with the reason that Windows uses this volume for virtual memory

### Requirement: Locked BitLocker volumes
A BitLocker-encrypted volume that is currently locked SHALL have every modifying operation disabled, with the reason that it must be unlocked in Windows first.

#### Scenario: Locked encrypted USB drive
- **WHEN** the user selects a locked BitLocker volume
- **THEN** all modifying actions are disabled with the reason "This volume is locked by BitLocker. Unlock it in File Explorer first."

### Requirement: Disks managed by other Windows features
Dynamic disks and disks that are members of a Storage Spaces pool SHALL be shown as read-only in their entirety, with every disk, partition and volume operation disabled and the reason that Windows manages them through another tool.

#### Scenario: Storage Spaces member
- **WHEN** a disk belongs to a storage pool
- **THEN** it is listed with a lock indicator and the reason "Managed by Storage Spaces", and no modifying action is available on it or its contents

### Requirement: System disk restrictions
The disk containing the running Windows installation or its boot partitions SHALL have Initialize, Clear and Take offline disabled; operations on its non-protected partitions and unallocated space SHALL remain available.

#### Scenario: Clear system disk
- **WHEN** the user selects Disk 0, which holds Windows
- **THEN** Clear disk is disabled with the reason "Windows is installed on this disk"

#### Scenario: Data partition on system disk
- **WHEN** the system disk also holds an unprotected data volume E:
- **THEN** Delete and Format are available for E:

### Requirement: Read-only disks
A disk Windows reports as read-only SHALL have every modifying operation on it and its contents disabled, with the reason that the disk is read-only. Bring online is the only exception: it writes no data, so it SHALL stay available on an offline read-only disk.

#### Scenario: Write-protected SD card
- **WHEN** an SD card with its write-protect switch enabled is selected
- **THEN** all modifying actions are disabled with the reason that the disk is write-protected

#### Scenario: Offline and read-only disk
- **WHEN** a disk is both offline and read-only (for example because of Windows' storage policy)
- **THEN** Bring online is available, and after bringing it online the disk stays read-only with every other modifying action disabled as write-protected

### Requirement: No override in MVP
The MVP SHALL NOT offer any setting or action to unlock protected items.

#### Scenario: No advanced unlock
- **WHEN** the user looks through the app's menus and settings
- **THEN** there is no option to bypass protection
