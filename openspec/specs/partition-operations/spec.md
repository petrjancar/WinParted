# partition-operations Specification

## Purpose

Lets users create, delete, format and resize partitions and volumes, and change their drive letters and labels.

## Requirements

### Requirement: Create partition
The system SHALL let the user create a partition in a selected unallocated region, with size defaulting to the whole region, adjustable between 1 MB and the region size in MB or GB.

#### Scenario: Create using all free space
- **WHEN** the user selects 100 GB of unallocated space, chooses Create partition and accepts the defaults
- **THEN** a partition of about 100 GB is created, formatted and assigned a drive letter

#### Scenario: Size limits are exact
- **WHEN** the largest possible size is 14.43 GB
- **THEN** the dialog states the limit as "14.43 GB" (as precise as the input), and a Maximum button fills in exactly the largest size

#### Scenario: Create a smaller partition
- **WHEN** the user sets the size to 40 GB in a 100 GB region
- **THEN** a 40 GB partition is created at the start of the region and about 60 GB stays unallocated

#### Scenario: MBR partition limit reached
- **WHEN** an MBR disk cannot hold another partition
- **THEN** Create partition is disabled with the reason that MBR disks are limited to four primary partitions

### Requirement: Format options on create
The Create partition dialog SHALL offer formatting with file system, volume label, drive letter and quick format, defaulting to NTFS, no label, the first free drive letter and quick format on, and SHALL offer leaving the partition unformatted.

#### Scenario: Create without formatting
- **WHEN** the user chooses "Don't format" and confirms
- **THEN** a partition is created without a file system and without a drive letter, even if Windows assigned one on its own during creation

### Requirement: Supported file systems
The system SHALL offer NTFS, exFAT and FAT32 for formatting, with FAT32 only for volumes of 32 GB or less, matching what Windows itself offers; an unavailable choice SHALL be shown disabled with a reason. ReFS SHALL NOT be offered for formatting in the MVP; existing ReFS volumes remain shown and resizable.

#### Scenario: FAT32 on a 64 GB partition
- **WHEN** the target partition is 64 GB
- **THEN** FAT32 is disabled with the reason that WinParted, like Windows, formats FAT32 only up to 32 GB, and exFAT is suggested

#### Scenario: No ReFS option
- **WHEN** the user opens the Format or Create partition dialog
- **THEN** the file system choices are NTFS, exFAT and FAT32 only

### Requirement: Delete partition
The system SHALL let the user delete a partition, including partitions with unrecognized file systems, leaving unallocated space.

#### Scenario: Delete a data partition
- **WHEN** the user confirms Delete on "Data (D:)"
- **THEN** the partition is removed, its space shown as unallocated (merged with adjacent unallocated space), and D: no longer exists

#### Scenario: Delete a Linux partition
- **WHEN** the user deletes a partition labelled "Unknown filesystem"
- **THEN** the confirmation states that the partition's contents cannot be read by Windows and will be erased, and on confirmation the partition is removed

### Requirement: Format volume
The system SHALL let the user format an existing partition, choosing file system, label, allocation unit size (default recommended) and quick or full format.

#### Scenario: Quick format as exFAT
- **WHEN** the user formats E: as exFAT with label "STICK" using quick format
- **THEN** E: becomes an empty exFAT volume labelled "STICK" with the same drive letter

#### Scenario: Full format explained
- **WHEN** the user turns quick format off
- **THEN** the dialog explains that a full format checks the whole partition for errors and can take a long time

#### Scenario: Quick and full for every file system
- **WHEN** the user formats a partition on a physical disk as NTFS, exFAT or FAT32
- **THEN** both quick and full format are available, and a full format shows its percentage progress

#### Scenario: Full NTFS on a virtual disk
- **WHEN** the target is on a virtual disk (VHD/VHDX) and NTFS is selected
- **THEN** full format is disabled with the reason that Windows can't do a full NTFS format on a virtual disk, and quick format stays available

### Requirement: Shrink volume
The system SHALL let the user shrink an NTFS or ReFS volume to any size between the minimum size Windows reports and its current size, showing that minimum and why it may be larger than the used space.

#### Scenario: Shrink to free space
- **WHEN** the user shrinks a 500 GB volume to 300 GB
- **THEN** the volume becomes 300 GB and 200 GB of unallocated space appears directly after it

#### Scenario: Minimum size being calculated
- **WHEN** the user opens Shrink on a large volume and Windows needs time to report the minimum size
- **THEN** the dialog shows that the smallest possible size is being calculated, keeps Shrink disabled until it is known, and Cancel stays available

#### Scenario: Minimum size explained
- **WHEN** a volume has 100 GB used but Windows reports a 180 GB minimum
- **THEN** the dialog shows 180 GB as the minimum and explains that some system files cannot be moved

### Requirement: Extend volume
The system SHALL let the user extend an NTFS or ReFS volume into unallocated space that directly follows it on the same disk, up to the full size of that space.

#### Scenario: Extend into following free space
- **WHEN** 50 GB of unallocated space follows D: and the user extends D: by 50 GB
- **THEN** D: grows by 50 GB and the unallocated space disappears

#### Scenario: Free space before the volume
- **WHEN** the only unallocated space on the disk is directly before D:
- **THEN** Extend is disabled with the reason that a volume can only grow into free space after it, and that moving partitions is not supported

### Requirement: Change drive letter
The system SHALL let the user assign, change or remove a volume's drive letter, offering only letters that are currently free.

#### Scenario: Change letter
- **WHEN** the user changes E: to X:
- **THEN** the volume is accessible as X: and E: is free

#### Scenario: Remove letter
- **WHEN** the user removes the drive letter from E:
- **THEN** the dialog warns that programs using E: may stop working, and after confirmation the volume has no drive letter

### Requirement: Change volume label
The system SHALL let the user change a volume's label, enforcing the file system's label length and character limits as the user types.

#### Scenario: Rename volume
- **WHEN** the user changes the label of D: from "New Volume" to "Projects"
- **THEN** D: is shown as "Projects (D:)" in the app and in Windows

#### Scenario: Label too long for FAT32
- **WHEN** the user types a 15-character label for a FAT32 volume
- **THEN** the dialog shows that FAT32 labels are limited to 11 characters and the confirm button is disabled
