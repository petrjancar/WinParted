# Spec Delta

## ADDED Requirements

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
