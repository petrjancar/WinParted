# disk-operations Specification

## Purpose

Lets users prepare and reset whole disks: initialize a new disk with a partition table, take a disk online or offline, and clear a disk of all its partitions.

## Requirements

### Requirement: Initialize disk
The system SHALL let the user initialize a disk that has no partition table, choosing GPT or MBR, with GPT preselected and a short plain-language explanation of the difference.

#### Scenario: Initialize new disk as GPT
- **WHEN** the user selects an uninitialized disk, chooses Initialize, keeps GPT and confirms
- **THEN** the disk gets a GPT partition table and is shown as a 16 MB Microsoft Reserved partition (created by Windows, locked) followed by one unallocated region

#### Scenario: MBR on a disk larger than 2 TB
- **WHEN** the user chooses MBR for a 4 TB disk
- **THEN** the dialog warns that only the first 2 TB will be usable before the user confirms

#### Scenario: Initialize not offered for initialized disk
- **WHEN** the selected disk already has a partition table
- **THEN** Initialize is not available, and the reason points to Clear disk

### Requirement: Online and offline
The system SHALL let the user take a disk offline and bring an offline disk online, and SHALL explain in the confirmation that taking a disk offline hides its volumes from Windows until it is brought online again.

#### Scenario: Take disk offline
- **WHEN** the user confirms Take offline for Disk 2 with volume F:
- **THEN** Disk 2 is shown as Offline and F: is no longer accessible in Windows

#### Scenario: Bring disk online
- **WHEN** the user chooses Bring online on an offline disk
- **THEN** the disk becomes Online and its partitions and volumes are shown

#### Scenario: Removable media can't go offline
- **WHEN** the selected disk is removable media, such as a USB flash drive
- **THEN** Take offline is disabled with the reason that Windows can't take removable media offline

#### Scenario: Offline disk details
- **WHEN** a disk is offline
- **THEN** its layout is shown as unavailable, with the offline reason Windows reports if any, and Bring online is the primary action

### Requirement: Clear disk
The system SHALL let the user remove all partitions and data from a disk, leaving it uninitialized, after a confirmation that shows the disk's name, size, bus type and a labelled partition bar with used space, and requires typing the disk number.

#### Scenario: Clear a USB stick
- **WHEN** the user chooses Clear disk on Disk 3, types "3" and confirms
- **THEN** all partitions on Disk 3 are removed and it is shown in the state Windows reports: "Not initialized", or, because Windows keeps a partition table on removable media, an empty MBR disk with only unallocated space

#### Scenario: Wrong number typed
- **WHEN** the user types "2" in the Clear disk confirmation for Disk 3
- **THEN** the confirm button stays disabled

#### Scenario: Confirmation shows what will be lost
- **WHEN** the Clear disk dialog opens for a disk containing "BACKUP (E:)" with 41 GB used
- **THEN** the dialog shows a partition bar labelled "BACKUP (E:)" with "41 GB used" and states that all partitions and files will be erased and this cannot be undone

### Requirement: Clear then initialize
After a successful Clear disk, the system SHALL offer the natural next step: initializing the disk if it ended up not initialized, or creating a partition if Windows kept an empty partition table.

#### Scenario: Offer after clear
- **WHEN** Clear disk completes and the disk is not initialized
- **THEN** the success notification includes an "Initialize now" action that opens the Initialize dialog for that disk

#### Scenario: Offer after clearing a USB stick
- **WHEN** Clear disk completes and Windows kept an empty MBR partition table (removable media)
- **THEN** the success notification includes a "Create partition" action for the free space instead of "Initialize now"

### Requirement: Turn off read-only
The system SHALL offer Turn off read-only for a disk Windows reports as read-only, online or offline, unless the disk is managed by another Windows feature (dynamic disk, Storage Spaces) or its protection can't be verified. After a confirmation, it SHALL clear the disk's read-only attribute through Windows. It SHALL NOT offer the action on a writable disk, and it SHALL NOT change a partition's own read-only attribute.

#### Scenario: Turn off read-only
- **WHEN** the user selects read-only Disk 4, chooses Turn off read-only and confirms
- **THEN** Disk 4 is shown without the Read-only marker, its actions become available as for any writable disk, and a notification says Disk 4 is no longer read-only

#### Scenario: Confirmation explains the change
- **WHEN** the Turn off read-only dialog opens for Disk 4
- **THEN** it names the disk (number, model, size), explains that Windows and programs will be able to change the disk and its volumes again, and that the flag may have been set on purpose by Windows or another tool; Cancel leaves the disk read-only

#### Scenario: Not offered on a writable disk
- **WHEN** the selected disk is not read-only
- **THEN** Turn off read-only is not shown

#### Scenario: Offline and read-only disk
- **WHEN** a disk is both offline and read-only
- **THEN** both Bring online and Turn off read-only are available, and each can be done without the other

#### Scenario: Managed disk
- **WHEN** a read-only disk is a member of a Storage Spaces pool
- **THEN** Turn off read-only is disabled with the reason that Storage Spaces manages the disk

### Requirement: Read-only by hardware
If the disk is still read-only after Turn off read-only, or Windows refuses the change, the system SHALL report that the disk couldn't be made writable, that nothing was changed, and that the disk is probably locked by its hardware or a Windows policy, suggesting to unlock a lock switch (as on SD cards) and reconnect the disk. The disk SHALL stay shown as read-only.

#### Scenario: SD card with its lock switch on
- **WHEN** the user turns off read-only for an SD card whose lock switch is on
- **THEN** a message says the card is still read-only, nothing was changed, and to slide its lock switch to unlocked and reconnect it; the card stays marked Read-only

#### Scenario: Disk image attached read-only
- **WHEN** the user turns off read-only for a virtual disk that Windows attached as read-only
- **THEN** the same message is shown and the disk stays marked Read-only
