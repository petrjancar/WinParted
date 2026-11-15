# Spec Delta

## ADDED Requirements

### Requirement: Back to the disk view
When a partition or unallocated region is selected, the system SHALL offer visible ways back to the disk's own details and actions: clicking the disk in the disk list (also when it is already selected), clicking the disk's name in the header, and a link with the disk's name at the top of the details panel. Pressing Escape anywhere in the main window SHALL do the same, unless a dialog is open or a text field has focus.

#### Scenario: Click the selected disk
- **WHEN** partition 2 of Disk 1 is selected and the user clicks Disk 1 in the disk list
- **THEN** no segment is selected and the details panel and actions show Disk 1

#### Scenario: Click the disk name in the header
- **WHEN** partition 2 of Disk 1 is selected and the user clicks "Disk 1" in the header
- **THEN** the details panel and actions show Disk 1; the name looks clickable on hover and is announced as "Disk 1, show disk details"

#### Scenario: Link in the details panel
- **WHEN** partition 2 of Disk 1 is selected
- **THEN** the top of the details panel shows "Disk 1 › Data (D:)", and clicking "Disk 1" shows the disk's details and actions

#### Scenario: Escape
- **WHEN** a segment is selected, no dialog is open, and the user presses Escape (with focus anywhere in the main window outside a text field)
- **THEN** the disk's details and actions are shown
