# Design

## Context

See proposal.md (Why) and the specs `installation`, `update-notice` and `app-info` for the required behaviour.

Current state that shapes the approach:

- **Release build:** `dotnet publish` produces a self-contained folder (`EnableMsixTooling` is required, see the MVP design), zipped by hand. Release, Debug and both test projects build in CI on `windows-latest`. The release zip itself is built on the developer PC.
- **Elevation:** Release builds always run elevated (`requireAdministrator`), Debug builds run as invoker and show the "needs administrator rights" screen. Disk work only happens elevated.
- **Files written today:** only `App.Log`, to `%LOCALAPPDATA%\WinParted\winparted.log` (or `WINPARTED_LOG`). Since the app runs elevated, that is the profile of whichever account approved the prompt. There are no settings yet.
- **Notifications:** `MainViewModel` already has a toast list (`ShowToast`, InfoBar-based, with an optional action) and an `IsBusy` flag while an operation runs.
- **About:** built in code in `MainWindow.OnAboutClick` (description, version, license; no link).
- **Core** is pure .NET and holds all logic that can be unit tested; the App layer only does Windows/UI work.

## Goals / Non-Goals

**Goals:**
- A reproducible installer built from the same published folder as the zip, by a script in the repository.
- Logic that decides "is there an update to announce" is pure and unit tested; the network call is a thin wrapper.
- A single, predictable place for every file WinParted writes, so uninstall can remove it completely.

**Non-Goals:**
- Building releases in CI and publishing them automatically (CI builds the installer to prove the script works; releasing stays a manual step).
- Removing data from *other* user profiles, or traces Windows itself keeps about any program (Prefetch, jump lists, crash reports).
- Settings UI beyond the two update switches.

## Decisions

### D1. Inno Setup 6 for the installer
Already chosen in the MVP design; confirmed here.
- **Why:** a single readable script; built-in admin install, x64 and minimum-Windows checks, upgrade detection by AppId, Restart Manager integration to close a running app, and `[UninstallDelete]` for removing data folders. Free and preinstalled on GitHub's `windows-latest` images.
- **Alternatives:** WiX/MSI: standard MSI behaviour (repair, GPO deployment), but far more XML for the same result, and removing runtime-created data folders needs custom actions. Velopack/Squirrel: per-user installs with self-update, which conflicts with a Program Files, admin-only app and with "no automatic install". NSIS: similar power, weaker scripting and upgrade handling.

### D2. Installer shape (`installer/WinParted.iss`)
- Fixed `AppId` GUID (never changes, it identifies the installation for upgrades and uninstall).
- `PrivilegesRequired=admin`, `ArchitecturesAllowed=x64os`, `ArchitecturesInstallIn64BitMode=x64os`, `MinVersion=10.0.22000`: Windows 10 and non-x64 PCs get Inno's standard "requires Windows 11 / 64-bit" message.
- `DefaultDirName={autopf}\WinParted`, Start menu group with the app and an Uninstall entry, `AppPublisher=WinParted contributors`, `AppVersion` passed in from the build (`/DAppVersion=x.y.z`) so the script never hard-codes it.
- Source is the published folder (`artifacts/WinParted-win-x64`); output `WinParted-<version>-setup.exe`.
- **Running app:** `CloseApplications=yes` (Restart Manager) plus `AppMutex=WinParted.Running`. The app creates that named mutex (`Global\` scope) at start; Inno then asks the user to close WinParted, during both install and uninstall.
- **Older installer over newer installation:** a short `[Code]` check compares the installed `DisplayVersion` from the uninstall registry key with `AppVersion` and asks for confirmation. Same version = Inno's normal reinstall (repair).
- **Uninstall:** `[UninstallDelete]` with `filesandordirs` for `{commonappdata}\WinParted` and `{localappdata}\WinParted` (the 0.1.0 log folder of the account running the uninstaller). Inno removes the installation folder, shortcuts and registry entry itself.

### D3. All runtime files in `%ProgramData%\WinParted`
- `settings.json` and `winparted.log` live in `Environment.SpecialFolder.CommonApplicationData\WinParted`. `WINPARTED_LOG` still overrides the log path (development only).
- **Why:** machine-wide, the same for every administrator account, and for the installed app and the zip; the uninstaller can always find it. Writing there needs administrator rights, which the app has whenever it does real work.
- **Alternatives:** `%LOCALAPPDATA%` (today): differs per elevating account, so uninstall can miss it (the over-the-shoulder case where another admin approves the prompt). Registry (HKLM) for settings: works, but the log still needs a folder, and one place is simpler to remove and to explain. Next to the exe: Program Files isn't a place to write at runtime, and it would differ between the zip and the installed app.
- **Non-elevated (Debug, or the "needs administrator rights" screen):** reading settings works; writes fail and are ignored (logged via `Debug.WriteLine` only). The About switches are disabled there (app-info spec).

### D4. Settings store
- `AppSettings` record in Core (`CheckForUpdates = true`, `IncludePrereleases = false`) with JSON parse/serialize; unknown fields are ignored, a missing, empty or corrupt file means defaults, never an error.
- `SettingsStore` in the App reads once at start and writes on every change (write to a temp file, then replace, so a crash can't leave half a file).
- **Why not `ApplicationData.LocalSettings`:** it requires a packaged app.

### D5. Update check
- **Request:** one `GET https://api.github.com/repos/petrjancar/WinParted/releases?per_page=30` with `User-Agent: WinParted/<version>`, `Accept: application/vnd.github+json`, 10 s timeout, started after the first disk load so it never competes with startup. Unauthenticated limit is 60 requests/hour per IP, plenty for one request per start.
- **Why the list endpoint and not `/releases/latest`:** `/latest` skips pre-releases, so it can't serve the "Include pre-releases" setting, and returns 404 while every release is a pre-release (today's state).
- **Decision logic in Core** (`UpdateCheck.FindNewer(releases, currentVersion, includePrereleases)`): ignore drafts, ignore pre-releases unless included, parse `tag_name` (optional leading `v`, three numeric parts; anything else is ignored), and return the highest version greater than the running one, or none. Unit tested, including 0.10.0 > 0.9.0 and malformed tags.
- **Running version:** the assembly version, which comes from `<Version>` in `Directory.Build.props`, the same source as the About dialog and the installer.
- **Failures:** any exception, non-200 status or unparsable JSON is logged once and otherwise ignored.

### D6. Notice through the existing toast list
- A new `ToastKind.Update` (informational InfoBar) with text "WinParted 0.3.0 is available" (plus "pre-release" when it is one) and the action "View release", which opens the release's `html_url` with the shell.
- If `IsBusy` is true when the result arrives, the view model holds it and shows it when `IsBusy` turns false (after the operation's own result toast). Shown at most once per start; closing it removes it until the next start.
- **Why:** InfoBar doesn't take focus, already has close and action buttons, and its existing automation notification satisfies the screen-reader requirement. A dialog would interrupt disk work.

### D7. About dialog
- Stays a code-built `ContentDialog`, adding a `HyperlinkButton` ("github.com/petrjancar/WinParted", `NavigateUri` to the repository) and two `ToggleSwitch`es bound to the settings store, saved on toggle. "Include pre-releases" is disabled while checks are off; both are disabled when not elevated.
- Strings go to `Texts.resx` like the rest of the UI; the dialog keeps the app's keyboard rule (Close is the highlighted button).

### D8. Build and release flow
- **CI:** after the existing publish step, run `iscc /DAppVersion=<version> installer\WinParted.iss` and upload the setup exe with the build artifact. Inno Setup 6 is preinstalled on `windows-latest`; if a future image drops it, the step installs it with `choco install innosetup`.
- **Release (manual, documented in README):** publish, zip, run `iscc` locally, attach both files to the draft release. The version comes from `Directory.Build.props` (read with `dotnet msbuild -getProperty:Version`), so it's set in one place.

## Risks / Trade-offs

- [SmartScreen warns about the unsigned installer (and Windows shows "Unknown publisher" in the elevation prompt)] → Same as the zip today. Code signing remains a separate decision; release notes keep the "More info → Run anyway" hint.
- [An over-the-shoulder admin uninstalls: the 0.1.0 log folder of the original user stays] → Only affects users who ran 0.1.0; the spec limits the promise to the account running the uninstaller. All newer data is in ProgramData and always removed.
- [GitHub API changes or rate limits] → Check fails silently (spec); the user still finds releases on GitHub.
- [A pre-release-only project shows no notice to users on default settings] → Accepted and stated in the proposal; publishing a full release fixes it.
- [Log grows without limit in ProgramData] → Unchanged from today; it's small (startup and errors only). Uninstall now removes it.
- [Deleting `{localappdata}\WinParted` could remove something unrelated with the same name] → Only WinParted ever used that folder name for this app; the risk is accepted.
- [Installer not tested on a clean PC (dropped for the MVP)] → Installing, upgrading and uninstalling are checked on the development PC, including a check that no files remain; the installer never touches disks or partitions.

## Migration Plan

1. 0.1.0 zip users: the new version ignores `%LOCALAPPDATA%\WinParted` and starts writing to `%ProgramData%\WinParted`. Settings start at defaults (there were none before).
2. Installing over a 0.1.0 zip isn't an upgrade (the zip was never registered); users can delete the old folder by hand. Release notes say so.
3. Rollback: uninstall and use the previous zip. Installing an older installer over a newer one is possible after the warning (D2).
