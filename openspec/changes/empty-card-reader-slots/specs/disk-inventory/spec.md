## ADDED Requirements

### Requirement: Drive without media
When Windows reports a disk as having no media (for example an empty slot of a card reader), the system SHALL show it as a drive without media instead of as an offline, uninitialized or 0-byte disk: a "No media" badge in place of its size, partition style, online and health status; a message to insert a card or disk in place of the layout; and only the details that apply without media.

#### Scenario: Empty card reader slot
- **WHEN** a USB card reader has a card in its microSD slot and nothing in its SD slot
- **THEN** the SD slot is listed with its number, name and bus type and a "No media" badge, without a size, partition style, Offline or health badge, and the card is listed as a normal disk

#### Scenario: Empty slot selected
- **WHEN** the user selects the empty slot
- **THEN** the main area says "No media. Insert a card or disk to use this drive." instead of the layout or the offline panel, and the details show bus type, status "No media", serial number and location, but no size, partition style, health or sector size

#### Scenario: Card inserted
- **WHEN** the user inserts a card into a slot shown as No media
- **THEN** within 3 seconds the slot shows the card's size, partitions and volumes like any other disk

#### Scenario: Card removed
- **WHEN** the user removes the card from a slot that stays connected
- **THEN** within 3 seconds the slot is shown as No media again, and if a partition on it was selected, the slot itself is selected

## MODIFIED Requirements

### Requirement: Back to the disk view
When a partition or unallocated region is selected, the system SHALL offer visible ways back to the disk's own details and actions: clicking the disk in the disk list (also when it is already selected) and a link with the disk's name at the top of the details panel. Pressing Escape anywhere in the main window SHALL do the same, unless a dialog is open or a text field has focus. The disk's name in the header is plain text, not a link.

#### Scenario: Click the selected disk
- **WHEN** partition 2 of Disk 1 is selected and the user clicks Disk 1 in the disk list
- **THEN** no segment is selected and the details panel and actions show Disk 1

#### Scenario: Click the disk name in the header
- **WHEN** partition 2 of Disk 1 is selected and the user points at or clicks "Disk 1" in the header
- **THEN** nothing happens: the name doesn't change on hover, shows no tooltip and keeps the partition selected

#### Scenario: Link in the details panel
- **WHEN** partition 2 of Disk 1 is selected
- **THEN** the top of the details panel shows "Disk 1 › Data (D:)", and clicking "Disk 1" shows the disk's details and actions

#### Scenario: Escape
- **WHEN** a segment is selected, no dialog is open, and the user presses Escape (with focus anywhere in the main window outside a text field)
- **THEN** the disk's details and actions are shown, and no tooltip with the key's name appears anywhere in the window
