# update-notice Specification

## Purpose

Tells users when a newer WinParted release is available on GitHub, without downloading or installing anything and without getting in the way of disk work.

## Requirements

### Requirement: Check for a newer release at startup
When update checks are on, WinParted SHALL ask GitHub for its published releases once each time it starts with administrator rights (not on the "needs administrator rights" screen), and compare the newest eligible release with the running version. Draft releases SHALL never count. The check SHALL run in the background and SHALL NOT delay the disk list or block any action.

#### Scenario: Newer release exists
- **WHEN** WinParted 0.2.0 starts, update checks are on, and release 0.3.0 is published on GitHub
- **THEN** the disk list appears as usual, and shortly after the update notice for 0.3.0 is shown

#### Scenario: Already up to date
- **WHEN** WinParted 0.3.0 starts and 0.3.0 is the newest eligible release
- **THEN** no notice is shown

### Requirement: Pre-releases follow the setting
A release marked as a pre-release on GitHub SHALL count as eligible only when "Include pre-releases" is on. Versions SHALL be compared numerically (0.10.0 is newer than 0.9.0).

#### Scenario: Pre-release ignored by default
- **WHEN** WinParted 0.2.0 starts with "Include pre-releases" off, and the only newer release is 0.3.0, marked as a pre-release
- **THEN** no notice is shown

#### Scenario: Pre-release included
- **WHEN** the same situation occurs with "Include pre-releases" on
- **THEN** the notice for 0.3.0 is shown and says it is a pre-release

### Requirement: Non-blocking notice
A newer release SHALL be announced by a dismissible notice in the main window reading "WinParted <version> is available", with a link that opens the release's GitHub page in the default browser. The notice SHALL NOT be a dialog, SHALL NOT take keyboard focus, and SHALL NOT download or install anything. Screen readers SHALL announce it once.

#### Scenario: Open the release page
- **WHEN** the user clicks the notice's link
- **THEN** the release page for that version opens in the default browser and WinParted stays as it was

#### Scenario: Dismiss
- **WHEN** the user closes the notice
- **THEN** it disappears and is not shown again until WinParted is restarted

### Requirement: Never during an operation
If a disk operation is running when the check finishes, the notice SHALL wait until the operation has ended and its result has been shown.

#### Scenario: Check finishes mid-format
- **WHEN** the check finds a newer release while a format is in progress
- **THEN** the notice appears only after the format has finished and its result is shown

### Requirement: Silent failures
If the check fails (no network, GitHub unavailable or rate-limited, unexpected response), WinParted SHALL show nothing, keep working normally, and write the reason to the diagnostic log.

#### Scenario: Offline
- **WHEN** WinParted starts without a network connection and update checks are on
- **THEN** no error or notice is shown and all disk features work

### Requirement: Update settings
WinParted SHALL offer two settings: "Check for updates when WinParted starts" (on by default) and "Include pre-releases" (off by default). Changes SHALL be saved immediately and SHALL apply from the next start. When update checks are off, WinParted SHALL NOT contact GitHub or any other server.

#### Scenario: Checks turned off
- **WHEN** the user turns off "Check for updates when WinParted starts" and restarts WinParted
- **THEN** WinParted makes no network request and shows no update notice

### Requirement: Minimal data sent
The update check SHALL be a single HTTPS request to the GitHub Releases API of the WinParted repository. It SHALL NOT send information about the PC's disks, the user or their settings, apart from what any HTTPS request carries (IP address) and a User-Agent naming WinParted and its version.

#### Scenario: What is sent
- **WHEN** an update check runs
- **THEN** exactly one request goes to `api.github.com` for the WinParted repository's releases, and it contains no disk or user information
