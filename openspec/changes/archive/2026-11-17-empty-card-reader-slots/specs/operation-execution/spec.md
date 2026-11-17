## MODIFIED Requirements

### Requirement: Unavailable actions explain why
Every disabled operation SHALL show a plain-language reason that names the specific obstacle and, where one exists, what the user can do about it.

#### Scenario: Extend blocked by following partition
- **WHEN** the user selects C: and a Recovery partition sits directly after it
- **THEN** Extend is disabled with the reason "C: can only grow into free space directly after it. The Recovery partition is in the way."

#### Scenario: Shrink on unsupported filesystem
- **WHEN** the user selects a FAT32 volume
- **THEN** Shrink and Extend are disabled with the reason that only NTFS and ReFS volumes can be resized

#### Scenario: Drive without media
- **WHEN** the user selects a card reader slot with no card in it
- **THEN** Initialize and Clear disk are shown disabled with the reason "There's no card or disk in this drive. Insert one first.", and Bring online and Take offline aren't offered

### Requirement: Operation results
On success the system SHALL refresh the layout and show a brief success notification; on failure it SHALL show a plain-language error explaining what happened, whether anything was changed, and what the user can do, with the technical error details available on demand.

#### Scenario: Not enough space
- **WHEN** Windows rejects a resize because the requested size doesn't fit
- **THEN** the user sees that Windows couldn't resize the volume to that size, that nothing was changed, and a suggestion to try a smaller change, with an expandable technical detail section

#### Scenario: Disk removed mid-operation
- **WHEN** the target disk is disconnected while a format is running
- **THEN** the user sees that the disk was disconnected during the format and that the volume may be incomplete and should be formatted again

#### Scenario: Drive letter taken during Create partition
- **WHEN** the user creates and formats a partition with drive letter D:, and Windows refuses D: because another drive took it in the meantime
- **THEN** the user sees that the partition was created and formatted but D: is already in use by another drive, that the disk was changed, and to use Change drive letter to give it a letter; the new partition is shown without a drive letter

#### Scenario: Windows assigned the requested letter first
- **WHEN** Windows reports the requested letter as in use, but it is in use by the partition the operation was giving it to
- **THEN** the operation is reported as successful
