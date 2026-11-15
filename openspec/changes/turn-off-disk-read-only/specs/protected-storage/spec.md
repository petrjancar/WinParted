# Spec Delta

## RENAMED Requirements

- FROM: `### Requirement: No override in MVP`
- TO: `### Requirement: No protection override`

## MODIFIED Requirements

### Requirement: Read-only disks
A disk Windows reports as read-only SHALL have every modifying operation on it and its contents disabled, with the reason that the disk is read-only and that Turn off read-only can make it writable. Bring online and Turn off read-only are the only exceptions: Bring online writes no data, so it SHALL stay available on an offline read-only disk, and Turn off read-only (disk-operations) changes only the read-only attribute.

#### Scenario: Write-protected SD card
- **WHEN** an SD card with its write-protect switch enabled is selected
- **THEN** all modifying actions except Turn off read-only are disabled with the reason that the disk is read-only

#### Scenario: Offline and read-only disk
- **WHEN** a disk is both offline and read-only (for example because of Windows' storage policy)
- **THEN** Bring online is available, and after bringing it online the disk stays read-only with every other modifying action except Turn off read-only disabled as read-only

### Requirement: No protection override
The system SHALL NOT offer any setting or action to unlock protected items (system partitions, the Windows volume, managed disks). Turning off a disk's read-only attribute is not an override: the attribute is a Windows disk setting the user may change, and it doesn't unlock any protected partition.

#### Scenario: No advanced unlock
- **WHEN** the user looks through the app's menus and settings
- **THEN** there is no option to bypass protection
