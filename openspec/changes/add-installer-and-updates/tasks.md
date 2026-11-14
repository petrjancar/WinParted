# Tasks

## 1. Design brief

- [x] 1.1 Write `docs/design/installer-and-updates.md` (About dialog contents and states, update notice content and behavior, sample data, deliverables) and link it from the MVP brief; verify every `app-info` and `update-notice` scenario with a visible result maps to an element or state in the brief
- [x] 1.2 Produce the designs from the brief (deliverables in section 9) and review them; record any decision that changes wording or behavior in the brief (and the specs if they disagree); verify the brief and specs still agree before starting group 5

## 2. App data folder

- [x] 2.1 Move `App.Log` to `%ProgramData%\WinParted\winparted.log` (keep the `WINPARTED_LOG` override; ignore write failures when not elevated); verify by running the Release build elevated in sample mode and finding the log there and nothing new under `%LOCALAPPDATA%\WinParted`
- [x] 2.2 Create the named mutex `Global\WinParted.Running` at app start (D2); verify a second running instance doesn't fail and the mutex is visible while the app runs (e.g. Inno's AppMutex check in group 5, or a small PowerShell `[Threading.Mutex]::OpenExisting` check)
- [x] 2.3 Update the README section on files: where WinParted writes (one machine-wide folder) and that the zip leaves it behind when deleted; verify the text matches the `installation` spec ("One machine-wide data folder")

## 3. Settings

- [x] 3.1 Add `AppSettings` (CheckForUpdates = true, IncludePrereleases = false) with JSON parse/serialize in Core; verify unit tests for defaults, round trip, unknown fields, and missing/empty/corrupt input falling back to defaults
- [ ] 3.2 Add the App `SettingsStore` (read once at start, save on change via temp file + replace, writes ignored when not elevated); verify by toggling a value in a Debug elevated run, restarting, and seeing it kept, and that a non-elevated Debug run starts with the saved values without errors

## 4. Update check

- [x] 4.1 Add `UpdateCheck.FindNewer(releases, currentVersion, includePrereleases)` and the release model in Core (D5); verify unit tests: newer full release found, same/older ignored, drafts ignored, pre-release ignored unless included, numeric compare (0.10.0 > 0.9.0), `v` prefix optional, malformed tags ignored, empty list
- [x] 4.2 Add the GitHub client in the App (single GET to the releases list, User-Agent `WinParted/<version>`, 10 s timeout, all failures logged and swallowed); verify by pointing it at the real repository from a Debug run (finds 0.1.0 only with pre-releases included) and by running with the network disabled (no error shown, reason in the log)
- [x] 4.3 Run the check once per elevated start, after the first disk load, only when `CheckForUpdates` is on; verify with a Debug elevated run and the log: with checks off it records "update check skipped" and no request; with checks on, exactly one request; on the "needs administrator rights" screen, none

## 5. Update notice and About

- [x] 5.1 Add `ToastKind.Update` and the update notice ("WinParted x.y.z is available", "pre-release" marker, "View release" opens `html_url`), held back while `IsBusy` and shown after the operation's result; strings in `Texts.resx`; verify with a test hook or a temporarily lowered version in a Debug build: notice appears, link opens the release page, close removes it, and starting a format before the check finishes delays the notice until the result is shown
- [ ] 5.2 Extend the About dialog: repository link (`github.com/petrjancar/WinParted`), "Check for updates when WinParted starts" and "Include pre-releases" switches bound to the settings store, pre-release switch disabled while checks are off, both disabled when not elevated; verify each `app-info` scenario by hand (including Tab order, Escape and Enter) and with Narrator for the accessible names
- [ ] 5.3 Check the built About dialog and update notice against `docs/design/installer-and-updates.md` and the approved designs (light, dark, High Contrast, minimum window size); fix differences or update the brief where the design changed; verify each deliverable in brief section 9 matches the running app

## 6. Installer

- [ ] 6.1 Write `installer/WinParted.iss` per D2 (fixed AppId, admin, x64, Windows 11 minimum, Program Files, Start menu with Uninstall entry, AppMutex, `AppVersion` from `/DAppVersion`, `[UninstallDelete]` for `{commonappdata}\WinParted` and `{localappdata}\WinParted`, downgrade warning in `[Code]`); verify `iscc` builds `WinParted-<version>-setup.exe` from the published folder without warnings
- [ ] 6.2 Add the installer to CI (run `iscc` after publish, install Inno Setup with choco only if missing, upload the setup exe with the build artifact); verify a pull request run produces the setup exe artifact
- [ ] 6.3 Document in README: install, update (run the new installer or replace the zip folder) and uninstall, plus the local release steps (publish, zip, `iscc` with the version from `dotnet msbuild -getProperty:Version`); verify the commands in README run as written

## 7. End-to-end checks on the development PC

- [ ] 7.1 Fresh install: run the installer (approve the prompt), then check Program Files, the Start menu entry, Installed apps (name, version, publisher) and that the app starts elevated and shows disks; also check that declining the prompt installs nothing (installation spec, "Installer per release")
- [ ] 7.2 Upgrade and downgrade: install a build with version 0.2.0, turn update checks off, install a build with a higher version (e.g. 0.2.1-test via `/DAppVersion`), verify settings are kept and Installed apps shows one entry; run the older installer and verify the warning; run the installer while WinParted is open and verify it asks to close it
- [ ] 7.3 Uninstall leaves nothing: with settings and a log present (and a `%LOCALAPPDATA%\WinParted` folder recreated to stand in for 0.1.0 data), uninstall from Settings → Apps while WinParted is open; verify it asks to close it, and afterwards that none of `Program Files\WinParted`, `%ProgramData%\WinParted`, `%LOCALAPPDATA%\WinParted`, the Start menu folder or the uninstall registry key exist
- [ ] 7.4 Update notice against the real GitHub: with a build whose version is lower than the newest release, verify the notice appears with pre-releases included and doesn't with the default settings; offline start shows nothing and works normally
- [ ] 7.5 Portable zip: extract and run the zip build; verify it runs, shares settings with the installed app (same data folder), and registers nothing in Installed apps or the Start menu

## Workflow follow-up

- Publish the next release with both the zip and the installer attached, and mention in the release notes that 0.1.0 zip folders can be deleted by hand.
- Archive the change after the release.
