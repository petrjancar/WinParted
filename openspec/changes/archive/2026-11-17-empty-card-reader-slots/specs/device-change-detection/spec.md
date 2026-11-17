## MODIFIED Requirements

### Requirement: Automatic refresh on device change
The system SHALL detect disks being connected, disconnected or modified while the application runs and SHALL refresh the displayed information automatically within 3 seconds, without user action. This includes media inserted into or removed from a drive that stays connected, such as a card reader slot, for which Windows sends no change notification.

#### Scenario: USB stick connected
- **WHEN** the user plugs in a USB stick while the app is open
- **THEN** the new disk appears in the list within 3 seconds and a brief non-blocking notification says a disk was connected

#### Scenario: USB stick removed
- **WHEN** the user unplugs a USB stick that is not selected
- **THEN** it disappears from the list and the current selection is unchanged

#### Scenario: Card reader connected
- **WHEN** the user plugs in a card reader with a card in one slot and nothing in the other
- **THEN** one notification per slot says a disk was connected: the card with its size, and the empty slot with "no media" instead of a size

#### Scenario: Card inserted into a connected reader
- **WHEN** the user inserts a card into an empty slot of a card reader that is already connected
- **THEN** within 3 seconds the slot shows the card, without a "connected" notification

#### Scenario: Checking only while needed
- **WHEN** no removable drive and no drive without media is connected, or the window is minimized, or an operation is running
- **THEN** the system doesn't check drives for inserted or removed media

### Requirement: Burst coalescing
The system SHALL treat a burst of change notifications caused by a single physical event as one refresh, and SHALL announce each connected or disconnected disk once, even when its notifications are spread over more than one refresh.

#### Scenario: Multiple notifications for one plug-in
- **WHEN** connecting one disk produces several change notifications within half a second
- **THEN** the app performs a single refresh

#### Scenario: Notifications spread over a refresh
- **WHEN** a second change notification arrives while a refresh caused by the first is still reading the disks
- **THEN** the next refresh starts after the first one finishes, and each connected disk is announced once
