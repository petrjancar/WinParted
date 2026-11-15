# Spec Delta

## REMOVED Requirements

### Requirement: Uninstall leaves no files
**Reason**: It also required deleting the per-user log folder of version 0.1.0 (`%LOCALAPPDATA%\WinParted`), which practically nobody used. The rest of the requirement continues as "Uninstall removes everything WinParted created".
**Migration**: None. A 0.1.0 log folder, if any, stays until the user deletes it.

## ADDED Requirements

### Requirement: Uninstall removes everything WinParted created
Uninstalling SHALL remove everything WinParted creates on the PC: the installation folder, Start menu entries, the installed-apps registration, and the app data folder with settings and the diagnostic log. It SHALL NOT remove anything else, and it SHALL NOT change any disk, partition or volume.

#### Scenario: Nothing left behind
- **WHEN** a user installs WinParted, runs it (creating settings and a log), and then uninstalls it
- **THEN** none of these exist any more: `Program Files\WinParted`, the WinParted Start menu entries, the WinParted app data folder, and WinParted's installed-apps registry entry
