## MODIFIED Requirements

### Requirement: Format options on create
The Create partition dialog SHALL offer formatting with file system, volume label, drive letter and quick format, defaulting to NTFS, no label, the first free drive letter and quick format on, and SHALL offer leaving the partition unformatted. A drive letter is free only if no drive holds it, including volumes without a partition such as an empty card reader slot.

#### Scenario: Create without formatting
- **WHEN** the user chooses "Don't format" and confirms
- **THEN** a partition is created without a file system and without a drive letter, even if Windows assigned one on its own during creation

#### Scenario: Letter of an empty card reader slot
- **WHEN** C: is the Windows volume and an empty card reader slot holds D:
- **THEN** the dialog offers E: as the default letter and doesn't offer D:

### Requirement: Change drive letter
The system SHALL let the user assign, change or remove a volume's drive letter, offering only letters that are currently free (held by no drive, including volumes without a partition such as an empty card reader slot), and SHALL leave the volume's current letter unchanged if the new letter turns out to be taken when the change runs.

#### Scenario: Change letter
- **WHEN** the user changes E: to X:
- **THEN** the volume is accessible as X: and E: is free

#### Scenario: Remove letter
- **WHEN** the user removes the drive letter from E:
- **THEN** the dialog warns that programs using E: may stop working, and after confirmation the volume has no drive letter

#### Scenario: Letter of an empty card reader slot not offered
- **WHEN** an empty card reader slot holds D: and the user opens Change drive letter for another volume
- **THEN** D: is not in the list of letters

#### Scenario: New letter taken in the meantime
- **WHEN** the user changes E: to X:, and another drive takes X: before the change runs
- **THEN** the user sees that X: is already in use by another drive and that nothing was changed, and the volume is still E:
