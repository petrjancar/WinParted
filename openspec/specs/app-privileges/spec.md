# app-privileges Specification

## Purpose

Ensures WinParted runs with the administrator rights that disk modifications require, and behaves clearly when those rights are not granted.

## Requirements

### Requirement: Administrator rights at launch
The application SHALL request administrator rights through the standard Windows elevation prompt when it starts.

#### Scenario: User approves elevation
- **WHEN** a user starts WinParted and approves the elevation prompt
- **THEN** the app opens with all features available

### Requirement: Elevation declined
If elevation is declined, the application SHALL NOT start, and Windows' standard behavior applies.

#### Scenario: User declines elevation
- **WHEN** the user clicks No on the elevation prompt
- **THEN** WinParted does not open and no disk is read or modified

### Requirement: Missing rights detected
If the application is somehow running without administrator rights, it SHALL show a clear message explaining that administrator rights are required and offer to restart with them, instead of showing failing operations.

#### Scenario: Launched without rights
- **WHEN** the app detects at startup that it is not elevated
- **THEN** it shows "WinParted needs administrator rights to manage disks" with a "Restart as administrator" action
