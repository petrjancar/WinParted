# Proposal

## Why

A disk that Windows reports as read-only is shown in WinParted, but every action on it is disabled and there is no way to make it writable again; users have to fall back to `diskpart` (`attributes disk clear readonly`). Turning the flag off was decided in the MVP as the next step for read-only disks ("option C") and is the last feature planned before 1.0.

Once a partition is selected, the disk's own details and actions can only be reached again with Escape in the partition layout, which nothing in the UI mentions; mouse users are stuck.

The installer also still deletes `%LOCALAPPDATA%\WinParted`, the log folder of version 0.1.0. That version had practically no users, so the extra cleanup and its explanations in the docs aren't worth keeping.

## What Changes

- New disk action **Turn off read-only**, shown on a disk Windows reports as read-only, online or offline. A confirmation explains what changes; WinParted then clears the disk's read-only attribute through Windows.
- **Hardware locks:** if the disk stays read-only (an SD card's lock switch, a write-protected USB stick, a VHDX attached read-only, or a Windows policy), WinParted says that it couldn't make the disk writable, that nothing was changed, and what to try (unlock the switch and reconnect the card).
- The read-only reason on disabled actions points to the new action.
- Only turning the flag **off**, and only the **disk** flag. Making a disk read-only and a partition's own read-only attribute stay out of scope.
- **Back to the disk view:** clicking the disk in the list (also the selected one), clicking the disk's name in the header, a "Disk 1 › Data (D:)" link at the top of the details panel, and Escape anywhere in the window (no dialog open, not in a text field) all return to the disk's details and actions.
- **Header without disk buttons:** the header's Initialize / Take offline / Clear disk buttons repeat the disk actions card; with the way back to the disk view they are no longer needed and are removed. The header shows the disk's name and status chips in rows.
- **Installer:** stop deleting `%LOCALAPPDATA%\WinParted` on uninstall; remove the related requirement scenario and the mentions in the docs.

## Capabilities

### New Capabilities
<!-- None. -->

### Modified Capabilities
- `disk-operations`: new requirement "Turn off read-only" (the action, its confirmation, success and hardware-lock outcomes).
- `protected-storage`: "Read-only disks" allows Turn off read-only next to Bring online and points the reason to it; "No override in MVP" is reworded so that clearing Windows' read-only attribute is not a protection bypass.
- `disk-inventory`: new requirement "Back to the disk view".
- `installation`: "Uninstall leaves no files" no longer covers the 0.1.0 per-user folder (scenario "Data from 0.1.0" removed).

## Impact

- **Core:** `ActionKind.ClearReadOnly`, availability rule and reason text, a request type, a dialog model, success and failure texts, tests.
- **Storage:** one SMP call, `MSFT_Disk.SetAttributes(IsReadOnly = false)`, and a re-read to detect a disk that stayed read-only.
- **App:** the action in the disk actions and its confirmation dialog, following the existing Take offline dialog.
- **App:** the disk list's click on the selected disk, the link in the details panel, the layout's hint text (moved to `Texts.resx`).
- **Installer:** one `[UninstallDelete]` line removed.
- **Docs:** MVP design brief (read-only disk state, new dialog), CONTRIBUTING.md (runtime files).
- **Testing:** needs a short spike on a VHDX (set read-only, turn off, attach read-only) and, if available, an SD card with a lock switch, to record what Windows returns.
