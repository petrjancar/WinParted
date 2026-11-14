# Spec Delta

## Purpose

Lets users install, upgrade and completely remove WinParted through a standard Windows installer, and defines where the app keeps the files it creates so that uninstalling leaves nothing behind.

## ADDED Requirements

### Requirement: Installer per release
Each release SHALL provide an installer (`WinParted-<version>-setup.exe`) next to the portable zip. The installer SHALL install WinParted for all users of the PC and SHALL require administrator rights.

#### Scenario: Fresh installation
- **WHEN** a user runs the installer on a PC without WinParted and approves the elevation prompt
- **THEN** WinParted is installed in `Program Files\WinParted`, a "WinParted" shortcut appears in the Start menu for all users, and WinParted is listed in Settings → Apps → Installed apps with its version and publisher

#### Scenario: Elevation declined
- **WHEN** the user declines the installer's elevation prompt
- **THEN** nothing is installed and nothing is written to the disk

### Requirement: No extra prerequisites
The installed app SHALL run without installing anything else: the .NET runtime and Windows App SDK are included. The installer SHALL refuse to install on Windows versions older than Windows 11 or on non-x64 PCs, and SHALL say why.

#### Scenario: Windows 10
- **WHEN** the installer runs on Windows 10
- **THEN** it explains that WinParted requires Windows 11 (x64) and exits without installing anything

### Requirement: Upgrade in place
Running the installer of a newer version over an existing installation SHALL replace the program files, keep the user's settings, and leave exactly one installed-apps entry showing the new version. If WinParted is running, the installer SHALL ask the user to close it before continuing.

#### Scenario: Upgrade keeps settings
- **WHEN** WinParted 0.2.0 is installed with update checks turned off, and the user runs the 0.3.0 installer
- **THEN** after installation About shows version 0.3.0, update checks are still off, and Installed apps lists WinParted once, as 0.3.0

#### Scenario: App running during upgrade
- **WHEN** the installer starts while WinParted is open
- **THEN** it asks the user to close WinParted, and continues only after it has closed

### Requirement: Installing over the same or an older version
Running an installer whose version is the same as the installed one SHALL repair the installation. Running an older installer over a newer installation SHALL warn that a newer version is installed and continue only if the user confirms.

#### Scenario: Older installer
- **WHEN** 0.3.0 is installed and the user runs the 0.2.0 installer
- **THEN** the installer warns that a newer version (0.3.0) is installed and asks whether to continue

### Requirement: Uninstall from Windows
WinParted SHALL be uninstallable from Settings → Apps → Installed apps and from the Uninstall entry in its Start menu folder. Uninstalling SHALL require administrator rights. If WinParted is running, the uninstaller SHALL ask the user to close it before continuing.

#### Scenario: Uninstall from Settings
- **WHEN** the user uninstalls WinParted from Settings → Apps and approves the elevation prompt
- **THEN** WinParted is removed and no longer appears in Installed apps or the Start menu

### Requirement: Uninstall leaves no files
Uninstalling SHALL remove everything WinParted created on the PC: the installation folder, Start menu entries, the installed-apps registration, the app data folder with settings and the diagnostic log, and the per-user data folder left by version 0.1.0 for the account that runs the uninstaller. It SHALL NOT remove anything else, and it SHALL NOT change any disk, partition or volume.

#### Scenario: Nothing left behind
- **WHEN** a user installs WinParted, runs it (creating settings and a log), and then uninstalls it
- **THEN** none of these exist any more: `Program Files\WinParted`, the WinParted Start menu entries, the WinParted app data folder, and WinParted's installed-apps registry entry

#### Scenario: Data from 0.1.0
- **WHEN** a user who ran the 0.1.0 zip (which wrote `%LOCALAPPDATA%\WinParted`) installs a later version and later uninstalls it
- **THEN** `%LOCALAPPDATA%\WinParted` of that user is removed too

### Requirement: One machine-wide data folder
WinParted SHALL keep all files it creates at runtime (settings and the diagnostic log) in a single machine-wide folder, the same for every user account and for both the installed app and the portable zip. It SHALL NOT write anywhere else, except the files the user explicitly saves.

#### Scenario: Settings shared across accounts
- **WHEN** an administrator turns off update checks, and a different administrator account then starts WinParted on the same PC
- **THEN** update checks are off for that account too

### Requirement: Portable zip remains available
Each release SHALL still provide the portable zip. It SHALL run without installation and SHALL NOT register itself in Installed apps or the Start menu.

#### Scenario: Portable use
- **WHEN** a user extracts the zip and runs `WinParted.exe`
- **THEN** WinParted runs with the same features as the installed app, and nothing appears in Installed apps or the Start menu
