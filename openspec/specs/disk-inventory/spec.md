# disk-inventory Specification

## Purpose

Lets users see every disk attached to the computer, how each disk is divided into partitions and volumes, and how much space is used, in a form that is easy to understand.

## Requirements

### Requirement: Disk list
The system SHALL list every disk reported by Windows, showing for each its disk number, friendly name, total size, bus type (for example USB, NVMe, SATA, Virtual), partition style (GPT, MBR or uninitialized), operational status and health status.

#### Scenario: Disks shown at startup
- **WHEN** the application starts on a computer with an internal NVMe disk and a USB stick
- **THEN** both disks are listed with number, name, size, bus type, partition style, status and health

#### Scenario: Uninitialized disk
- **WHEN** a disk has no partition table
- **THEN** it is listed with partition style "Not initialized"

#### Scenario: Unhealthy disk
- **WHEN** Windows reports a disk's health as Warning or Unhealthy
- **THEN** the disk is shown with a visible warning state and the reported health text

### Requirement: Partition layout bar
For each disk the system SHALL show a horizontal bar with every partition and every unallocated region larger than 1 MB, in on-disk order, with each segment's width proportional to its size and a minimum visible width so small partitions remain selectable.

#### Scenario: Mixed layout
- **WHEN** a disk contains an EFI partition, an MSR partition, a data volume and 20 GB of unallocated space at the end
- **THEN** the bar shows four segments in that order, with the unallocated segment visually distinct from partitions

#### Scenario: Tiny partition
- **WHEN** a disk contains a 16 MB MSR partition on a 2 TB disk
- **THEN** the MSR segment is still visible and can be selected

### Requirement: Segment labelling
Each partition segment SHALL show its drive letter (if any), volume label (if any) or a plain-language type name (for example "EFI System", "Recovery", "Unknown filesystem"), file system and size; volume segments SHALL show used and free space.

#### Scenario: Data volume
- **WHEN** a partition holds an NTFS volume labelled "Data" with letter D: that is 60% full
- **THEN** its segment shows "Data (D:)", "NTFS", its size and a fill indicator for 60% used

#### Scenario: Unrecognized filesystem
- **WHEN** a partition contains a filesystem Windows cannot read (for example ext4)
- **THEN** its segment is labelled "Unknown filesystem" with its size and no used/free values

### Requirement: Details panel
The system SHALL show a details panel for the selected disk, partition or unallocated region containing all properties relevant to that item, including identifiers (disk number, partition number, GPT type or MBR type, offset), sizes, file system, label, access paths and status flags.

#### Scenario: Select a partition
- **WHEN** the user selects a partition in the layout bar
- **THEN** the details panel shows its partition number, type, offset, size, file system, label, drive letter or mount paths, and used/free space

#### Scenario: Select unallocated space
- **WHEN** the user selects an unallocated region
- **THEN** the details panel shows its offset and size and the actions available for it

### Requirement: Manual refresh
The system SHALL let the user reload all disk information on demand, and SHALL keep the current selection if the selected item still exists afterwards.

#### Scenario: Refresh keeps selection
- **WHEN** the user has partition 2 of Disk 1 selected and triggers refresh
- **THEN** the information is reloaded and partition 2 of Disk 1 remains selected

#### Scenario: Selected item gone after refresh
- **WHEN** the selected partition no longer exists after refresh
- **THEN** the selection moves to the containing disk

### Requirement: Loading and error states
The system SHALL show a loading state while disk information is being read and SHALL show a plain-language error with a retry action if disk information cannot be read.

#### Scenario: Storage service unavailable
- **WHEN** reading disk information fails
- **THEN** the user sees an explanation and a "Try again" action instead of an empty list
