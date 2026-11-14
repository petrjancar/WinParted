# app-info Specification

## Purpose

Gives users basic facts about WinParted (version, license, where the project lives) and a place to change its update settings.

## Requirements

### Requirement: About dialog
The title bar SHALL have an About button that opens a dialog showing the app name, a one-sentence description, the running version, and the license (MIT). It SHALL be available on every screen, including the "needs administrator rights" screen.

#### Scenario: Open About
- **WHEN** the user clicks the About button in the title bar
- **THEN** a dialog shows "WinParted", its description, "Version <x.y.z>" matching the running build, and "MIT License"

### Requirement: Link to the GitHub repository
The About dialog SHALL contain a link labelled with the repository address (github.com/petrjancar/WinParted) that opens the repository page in the default browser.

#### Scenario: Open the repository
- **WHEN** the user clicks the repository link in About
- **THEN** https://github.com/petrjancar/WinParted opens in the default browser and the dialog stays open

### Requirement: Update settings in About
The About dialog SHALL show the two update settings, "Check for updates when WinParted starts" and "Include pre-releases", as switches reflecting their saved values. "Include pre-releases" SHALL be disabled while update checks are off. On the "needs administrator rights" screen both switches SHALL be shown but disabled, because settings can only be saved with administrator rights.

#### Scenario: Change a setting
- **WHEN** the user turns off "Check for updates when WinParted starts" in About and closes the dialog
- **THEN** the setting is saved, and the next start makes no update check

#### Scenario: Dependent setting
- **WHEN** "Check for updates when WinParted starts" is off
- **THEN** "Include pre-releases" is shown but disabled

### Requirement: Keyboard and screen reader access
Every element of the About dialog (link, switches, Close) SHALL be reachable with the keyboard and have an accessible name. Escape SHALL close the dialog, and Enter SHALL press its highlighted Close button, as in every other dialog.

#### Scenario: Keyboard only
- **WHEN** a keyboard-only user opens About and presses Tab
- **THEN** focus moves through the repository link, both switches and Close, and each is announced with its name
