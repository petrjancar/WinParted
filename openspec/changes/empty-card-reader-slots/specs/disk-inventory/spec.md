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
