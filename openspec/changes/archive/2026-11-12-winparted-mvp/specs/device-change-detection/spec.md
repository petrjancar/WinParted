# Spec Delta

## Purpose

Keeps the displayed disk information current when disks are connected, disconnected or changed outside the app, without disrupting work on unaffected disks.

## ADDED Requirements

### Requirement: Automatic refresh on device change
The system SHALL detect disks being connected, disconnected or modified while the application runs and SHALL refresh the displayed information automatically within 3 seconds, without user action.

#### Scenario: USB stick connected
- **WHEN** the user plugs in a USB stick while the app is open
- **THEN** the new disk appears in the list within 3 seconds and a brief non-blocking notification says a disk was connected

#### Scenario: USB stick removed
- **WHEN** the user unplugs a USB stick that is not selected
- **THEN** it disappears from the list and the current selection is unchanged

### Requirement: Burst coalescing
The system SHALL treat a burst of change notifications caused by a single physical event as one refresh.

#### Scenario: Multiple notifications for one plug-in
- **WHEN** connecting one disk produces several change notifications within half a second
- **THEN** the app performs a single refresh

### Requirement: Selected disk removed
When the selected disk, or the disk containing the selected item, disappears, the system SHALL select the first remaining disk (never leaving the main area empty) and tell the user the disk was disconnected and which disk is shown now.

#### Scenario: Selected disk unplugged
- **WHEN** the user has a partition on Disk 3 selected and unplugs Disk 3
- **THEN** Disk 0 (the first remaining disk) is selected and a notification says "Disk 3 was disconnected. Showing Disk 0 instead."

#### Scenario: Last disk unplugged
- **WHEN** the only remaining disk is unplugged
- **THEN** the "No disks found" state is shown and a notification says the disk was disconnected

### Requirement: Open dialog for an affected disk
When the disk targeted by an open operation dialog is removed or changed, the system SHALL close the dialog without running the operation and SHALL explain why.

#### Scenario: Disk changes while confirm dialog is open
- **WHEN** the Clear disk dialog for Disk 3 is open and Disk 3 is unplugged
- **THEN** the dialog closes, nothing is executed, and the user sees "This disk was disconnected or changed. Nothing was modified."

#### Scenario: Unrelated disk changes while dialog is open
- **WHEN** the Format dialog for a volume on Disk 1 is open and a USB stick is plugged in
- **THEN** the dialog stays open and unaffected while the disk list updates behind it

### Requirement: Changes during a running operation
While an operation is running, device changes on other disks SHALL update the list without affecting the operation.

#### Scenario: Other disk connected during format
- **WHEN** a format is running on Disk 1 and the user plugs in a USB stick
- **THEN** the stick appears in the list and the format continues unaffected
