# Spec Delta

## Purpose

Defines the common lifecycle every disk-modifying operation follows (availability, confirmation, re-verification, immediate execution, progress and errors) so all operations are predictable and safe.

## ADDED Requirements

### Requirement: Actions available per selection
The system SHALL offer only the operations relevant to the selected item type (disk, partition, or unallocated region), and SHALL show operations that are relevant but not currently possible as disabled rather than hiding them.

#### Scenario: Unallocated region selected
- **WHEN** the user selects an unallocated region
- **THEN** "Create partition" is offered and partition-only actions such as Format and Delete are not shown

### Requirement: Unavailable actions explain why
Every disabled operation SHALL show a plain-language reason that names the specific obstacle and, where one exists, what the user can do about it.

#### Scenario: Extend blocked by following partition
- **WHEN** the user selects C: and a Recovery partition sits directly after it
- **THEN** Extend is disabled with the reason "C: can only grow into free space directly after it. The Recovery partition is in the way."

#### Scenario: Shrink on unsupported filesystem
- **WHEN** the user selects a FAT32 volume
- **THEN** Shrink and Extend are disabled with the reason that only NTFS and ReFS volumes can be resized

### Requirement: Confirmation before modification
Every operation that modifies a disk SHALL require explicit confirmation in a dialog that states, in plain language, what will change and what data will be lost, naming the target by drive letter, label and disk number.

#### Scenario: Delete confirmation names the data
- **WHEN** the user chooses Delete on volume "Photos (E:)" with 120 GB used on Disk 2
- **THEN** the dialog says that deleting "Photos (E:)" on Disk 2 permanently erases all its files (120 GB used) and offers Cancel and Delete

#### Scenario: Programs using the volume
- **WHEN** the user opens the Format or Delete confirmation for "Data (D:)"
- **THEN** the dialog also warns that programs using Data (D:) will lose access to it and may lose unsaved work, and suggests closing them first, because Windows disconnects them instead of refusing the operation

#### Scenario: Cancel does nothing
- **WHEN** the user cancels any confirmation dialog
- **THEN** no change is made to any disk

#### Scenario: Same keyboard behaviour in every dialog
- **WHEN** the user presses Enter in any dialog
- **THEN** Enter presses the highlighted button: the confirm button when the input is valid (for example after typing the right disk number in Clear disk or a valid size in Create partition, which is used exactly as typed), nothing while the input is invalid, and Cancel in Delete partition, which has nothing to type; Escape always cancels

### Requirement: Immediate execution
A confirmed operation SHALL be executed immediately; the system SHALL NOT keep a queue of pending operations.

#### Scenario: Confirmed format runs at once
- **WHEN** the user confirms a format
- **THEN** the format starts immediately and the layout reflects the result as soon as it finishes

### Requirement: Target re-verification
Immediately before executing, the system SHALL verify that the target disk, partition or volume is still the same object the user confirmed (stable identity, not disk number) and is unchanged in a way that affects the operation; otherwise it SHALL abort without modifying anything.

#### Scenario: Disk number reassigned
- **WHEN** the user confirms Clear disk for Disk 2, and before execution that disk was removed and a different disk now has number 2
- **THEN** the operation is aborted and the user is told the disk changed and nothing was modified

### Requirement: One operation at a time
The system SHALL run at most one modifying operation at a time; while it runs, all modifying actions SHALL be unavailable, while browsing and selecting remain possible.

#### Scenario: Second operation blocked
- **WHEN** a shrink is running on Disk 0 and the user selects a volume on Disk 1
- **THEN** the user can view its details but modifying actions are disabled with the reason that another operation is in progress

### Requirement: Progress feedback
While an operation runs, the system SHALL show which operation is running on which target, show percentage progress when Windows reports it and an indeterminate indicator otherwise, and keep the app responsive.

#### Scenario: Long full format
- **WHEN** a full (non-quick) format is running
- **THEN** the user sees "Formatting Data (D:)" with progress, and the window can still be moved, resized and browsed

### Requirement: Operation results
On success the system SHALL refresh the layout and show a brief success notification; on failure it SHALL show a plain-language error explaining what happened, whether anything was changed, and what the user can do, with the technical error details available on demand.

#### Scenario: Not enough space
- **WHEN** Windows rejects a resize because the requested size doesn't fit
- **THEN** the user sees that Windows couldn't resize the volume to that size, that nothing was changed, and a suggestion to try a smaller change, with an expandable technical detail section

#### Scenario: Disk removed mid-operation
- **WHEN** the target disk is disconnected while a format is running
- **THEN** the user sees that the disk was disconnected during the format and that the volume may be incomplete and should be formatted again

### Requirement: Close during operation
The system SHALL warn the user if they try to close the application while an operation is running and SHALL NOT abandon the operation silently.

#### Scenario: Close attempt during shrink
- **WHEN** the user closes the window during a shrink
- **THEN** a dialog explains the operation is still running and the app stays open until it finishes or the user explicitly chooses to close anyway
