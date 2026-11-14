# Proposal

## Why

WinParted 0.1.0 ships only as a portable zip: users pick a folder themselves, get no Start menu entry, have no clean way to remove the app, and never learn that a newer version exists. An installer with a matching uninstaller and an in-app update notice were planned for the version after the MVP (MVP design, "Distribution and updates").

## What Changes

- **Installer** (`WinParted-<version>-setup.exe`, attached to each GitHub release next to the zip): installs WinParted for all users into Program Files, adds a Start menu shortcut, and registers it in Windows' installed apps list. Running a newer installer upgrades the existing installation in place and keeps the user's settings.
- **Uninstaller**: started from Windows Settings → Apps or the Start menu. It removes everything WinParted put on the disk: program files, shortcuts, the installed-apps entry, settings and the diagnostic log. No WinParted files or folders are left behind.
- **App data in one machine-wide place**: settings and the diagnostic log move from the current user's profile (`%LOCALAPPDATA%\WinParted`) to a single machine-wide folder, so the uninstaller can remove them reliably whichever administrator account runs it. The uninstaller also removes the old per-user folder left by 0.1.0.
- **Update notice**: when WinParted starts, it asks GitHub for the newest release. If it's newer than the running version, a non-blocking notice says "WinParted x.y.z is available" with a link to the release page. Nothing is downloaded or installed automatically, and the check never delays startup or interrupts an operation. Failures (offline, GitHub unavailable) are silent.
- **Update settings**: "Check for updates when WinParted starts" (on by default) and "Include pre-releases" (off by default).
- **About dialog**: gets a link to the GitHub repository and the two update settings.
- The portable zip stays available. It gets the same update notice and settings, but no uninstaller.

Consequence of the pre-release default: every current release (including 0.1.0) is a pre-release, so users with default settings get no notice until a release is published without the pre-release flag.

Out of scope: code signing (SmartScreen will still warn about the unsigned installer), automatic download or installation of updates, winget, MSIX, per-user (non-admin) installation, turning off a disk's read-only flag.

## Capabilities

### New Capabilities
- `installation`: installing, upgrading and uninstalling WinParted, including what the uninstaller removes and where WinParted keeps its settings and log.
- `update-notice`: checking GitHub for a newer release at startup, showing the notice, and the settings that control it.
- `app-info`: the About dialog: version, license, link to the GitHub repository, and access to the update settings.

### Modified Capabilities
<!-- None: no existing requirement changes. The app-privileges requirements apply unchanged to the installed app. -->

## Impact

- **New build output:** an Inno Setup script in the repository and a CI step that builds the installer from the published folder; releases attach both the zip and the installer.
- **App code:** `App.Log` writes to the new machine-wide folder; a small settings store; an update checker (HTTPS request to the GitHub Releases API, version comparison, pre-release filter); the notice in the main window; the About dialog in `MainWindow`.
- **Network:** the first time WinParted talks to the internet. One unauthenticated request to `api.github.com` per start while the check is on; no data about disks or the user is sent.
- **Docs:** README (install, update and uninstall instructions), the release notes template.
- **New dependency:** Inno Setup (build-time only, used in CI and for local release builds).
