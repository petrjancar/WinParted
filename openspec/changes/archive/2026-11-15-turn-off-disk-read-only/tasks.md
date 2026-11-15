# Tasks

## 1. Spike

- [x] 1.1 Add a `readonly` spike command (VHDX only, same safety checks as `behavior`): set a VHDX disk read-only with `SetAttributes(IsReadOnly=true)`, then clear it online and offline; attach a VHDX read-only (`Mount-DiskImage -Access ReadOnly`) and try to clear it; record return codes, `ExtendedStatus` and whether `IsReadOnly` changed in `docs/spikes/smp-behavior.md`; verify by running it in Terminal (Admin) (developer) and reading the recorded results
- [x] 1.2 Optional, if an SD card with a lock switch is available: with the switch locked, record what `SetAttributes(IsReadOnly=false)` returns and whether `IsReadOnly` stays true (read-only check via the spike, nothing written); verify the result is in `docs/spikes/smp-behavior.md` or noted as not tested

## 2. Core

- [x] 2.1 Add `ActionKind.ClearReadOnly`, `ClearReadOnlyRequest`, the availability rule (relevant only for read-only disks, listed first; exempt from the read-only lock, not from dynamic, Storage Spaces or unknown protection; offline per the spike result) and the texts (`Ui.Action.ClearReadOnly`, `Done.ClearReadOnly`, updated `Reason.DiskReadOnly`); verify unit tests for each `Turn off read-only` and updated `Read-only disks` scenario
- [x] 2.2 Add `ClearReadOnlyDialogModel` (title, identity, body, primary text; Enter confirms); verify a `DialogModelTests` case
- [x] 2.3 Add the "still read-only" outcome (D2): after a `ClearReadOnlyRequest`, a disk that is still read-only makes the result a failure with `Error.StillReadOnly` (nothing changed, raw code kept in details); verify `OperationRunnerTests` for success, still read-only after success, and a refused call

## 3. Storage and App

- [x] 3.1 Write the wording first: `docs/design/mvp-features.md` sections 4.2 (read-only state), 5 (lock table), 6.12 (the dialog, success and still-read-only outcomes), 7.1 (reason) and 8.1 (error); verify every `Turn off read-only` and `Read-only by hardware` scenario with visible text maps to wording in the brief
- [x] 3.2 Execute `ClearReadOnlyRequest` in `SmpStorageProvider` with `MSFT_Disk.SetAttributes(IsReadOnly=false)`; add an integration test on a read-only VHDX (set, clear, verify writable); verify by running the integration tests in Terminal (Admin) (developer)
- [x] 3.3 Wire the action in the window with the brief's wording: disk actions and the offline view's Turn off read-only button, the confirmation dialog, the success and failure messages; verify in sample mode with a read-only sample disk (add one if none) in light and dark, including Enter/Escape and the disabled state on a managed disk, and that the texts match brief sections 6.12, 7.1 and 8.1

## 4. Back to the disk view

- [x] 4.1 Write the wording in `docs/design/mvp-features.md` section 3.5 (breadcrumb, disk list click, Escape and its screen-reader hint); verify each `Back to the disk view` scenario maps to it
- [x] 4.2 Back to the disk view: clicking the disk in the disk list (also the selected one), the header's disk name (a button only while a segment is selected, styled as the heading, with hover, tooltip "Show disk details", automation name "Disk 1, show disk details"), the details panel's "Disk 1 › …" link, and a window-level Escape (not while a dialog is open or in a text field) clear the segment selection; move the layout bar's automation name to `Texts.resx` with the Escape hint; verify in sample mode: select a partition, then each of list click, header click, link and Escape (with focus on the layout, the actions card and the disk list) returns to the disk view, Escape in a dialog only closes the dialog, the link and header are reachable with Tab, and the breadcrumb isn't shown in the disk view
- [x] 4.3 Write the header change in `docs/design/mvp-features.md` section 3.3 (rows, no disk buttons) and redraw the design canvas's main-window boards to match; verify the brief, the canvas and design D6 agree
- [x] 4.4 Remove the disk action buttons and their reason line from the header (the actions card and the offline panel's buttons stay); verify in sample mode that each disk action is still reachable from the disk view, also after selecting a partition and going back, and that the offline panel still offers Bring online
- [x] 4.5 Retake the README screenshots if the breadcrumb or read-only UI is visible in them (`pwsh docs/images/make-screenshots.ps1`); verify the images show the current UI

## 5. Installer and docs

- [x] 5.1 Remove the `{localappdata}\WinParted` uninstall entry, its comment and `UsedUserAreasWarning=no` from `installer/WinParted.iss`, and the 0.1.0 sentence from CONTRIBUTING.md; verify `grep -ri localappdata` finds nothing outside `openspec/changes/archive/` and CI builds the installer
- [x] 5.2 Update README.md if needed (features list: turning off read-only); verify against the docs rule in AGENTS.md (screenshots only if the main window changed)

## 6. End-to-end check (developer)

- [x] 6.1 End-to-end: the developer's check of an elevated Release build, plus the integration tests (Terminal (Admin)) for both outcomes on real Windows: a VHDX made read-only becomes writable, and a VHDX attached read-only fails with the hardware-lock message and stays read-only (replaces the manual diskpart steps)

## Workflow follow-up

- Archive the change, then release 1.0.0 as a full release (README status line, release notes, clean-VM install as discussed).
