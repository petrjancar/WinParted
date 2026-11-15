# Design

## Context

See proposal.md (Why) and the delta specs for behavior.

- Read-only today: `Disk.IsReadOnly` comes from `MSFT_Disk.IsReadOnly`. `ProtectionClassifier` turns it into `DiskProtectionFlags.ReadOnly`, a whole-disk lock: `ActionAvailability.WholeDiskLock` disables everything with `ReasonCode.DiskReadOnly`, except Bring online, which `CheckDisk` exempts.
- Disk actions come from `ActionAvailability.RelevantActions` (Initialize, Bring online or Take offline, Clear disk) and render generically in the header and the actions card. An offline disk also shows a centered **Bring online** button.
- Bring online is the closest model: a request (`SetDiskOnlineRequest`), one SMP call in `SmpStorageProvider.Execute`, then the normal refresh and "Done" notification. Take offline adds a simple confirmation (`OfflineDialogModel`).
- `StorageErrorMapper` already maps 41002 ("disk is read-only") to a plain-language error.

## Goals / Non-Goals

**Goals:**
- One new disk action that reuses the existing operation pipeline (availability, confirmation, runner, refresh, notifications).
- A reliable "still read-only" outcome that doesn't depend on Windows returning an error.

**Non-Goals:**
- Making a disk read-only, partition-level read-only attributes, Windows storage policies (SAN policy, `WriteProtect` registry policy).
- A new design round: the dialog and button follow the existing Take offline / Bring online patterns; the MVP brief is updated with the wording.

## Decisions

### D1. Clear the attribute with `MSFT_Disk.SetAttributes(IsReadOnly = false)`
- The Storage Management API method behind `Set-Disk -IsReadOnly $false` and diskpart's `attributes disk clear readonly`. Same session and invoke helper as Online/Offline.
- **Alternatives:** diskpart scripts (text parsing, a second tool), `IOCTL_DISK_SET_DISK_ATTRIBUTES` (Win32 interop for something SMP already does).

### D2. Detect "still read-only" by re-reading, not by error codes
- After the call the runner refreshes the snapshot anyway. A new check in Core compares: if the target disk is still `IsReadOnly`, the operation result becomes a failure with the hardware-lock message (`Error.StillReadOnly`), `MayHaveChanged = false`.
- **Why:** hardware locks behave differently by device and driver: `SetAttributes` may return an error, or return success while the medium stays write-protected. Re-reading covers both. Known error codes (e.g. 41002, access denied) still map through `StorageErrorMapper`, but the operation's message is always the hardware-lock text, with the raw code in Technical details.
- The spike (task 1.1) records the actual return values so the error mapping and the docs are accurate.

### D3. Availability rule
- `ActionKind.ClearReadOnly` is relevant only when `disk.IsReadOnly`, listed first among the disk actions (it unblocks the others).
- In `CheckDisk` it is exempt from the read-only flag, like Bring online, but not from the other whole-disk locks (dynamic disk, Storage Spaces, protection unknown), which keep their reasons.
- Offline disks: available too, and shown next to Bring online in the offline view. SMP accepts attribute changes on offline disks (to confirm in the spike; if not, the action is disabled while offline with `ReasonCode.DiskOffline` and the spec scenario is adjusted).

### D4. Confirmation and texts
- `ClearReadOnlyDialogModel`, modeled on `OfflineDialogModel`: title "Turn off read-only for Disk {n}?", the disk identity line, body explaining that Windows and programs can change the disk and its volumes again and that the flag may have been set on purpose, primary button "Turn off read-only". Not destructive: normal accent button, Enter confirms.
- `Done.ClearReadOnly`: "Disk {n} is no longer read-only."
- `Reason.DiskReadOnly` becomes "This disk is read-only. Use Turn off read-only to make it writable; if it has a lock switch, unlock it first."
- `Error.StillReadOnly`: "Disk {n} is still read-only. It's probably locked by its hardware or a Windows policy. If it has a lock switch (like SD cards), slide it to unlocked and reconnect the disk. Nothing was changed."

### D5. Back to the disk view
- **Disk list:** `IsItemClickEnabled` with an `ItemClick` handler: a click on the disk that is already selected clears the segment selection (selecting another disk already does). `SelectionChanged` stays for keyboard and other disks.
- **Details panel:** when a segment is selected, a breadcrumb line above the title: a `HyperlinkButton` with the disk name ("Disk 1"), then "›" and the segment title. Clicking it clears the segment selection. Not shown in the disk view.
- **Header:** while a segment is selected, the disk name (`h1`) becomes a button styled as the heading (plain text in the disk view, where it would do nothing), with a hover highlight, hand cursor, tooltip "Show disk details" and automation name "Disk 1, show disk details"; clicking it clears the segment selection.
- **Escape:** a window-level `KeyboardAccelerator` (Escape) clears the segment selection. It isn't handled while a `ContentDialog` is open (the dialog handles Escape first) or when focus is in a `TextBox`. `LayoutBar` keeps its own Escape. Its automation name moves to `Texts.resx` and adds "Escape to show the disk".
- **Alternative rejected:** clicking the selected segment again to deselect it. Nobody expects it, and it's easy to trigger by accident.

### D6. No disk buttons in the header
- The header's Initialize / Take offline / Clear disk buttons and their reason line repeat the disk actions card; they only existed so disk actions stayed reachable with a partition selected, which D5 now covers. They are removed: the actions card is the one place for the selection's actions, and the header shows the disk's name and status chips in rows. The offline panel's centered buttons stay.
- **Alternative rejected:** keeping the header buttons and dropping the disk view's actions card, which would leave the disk view without an actions area and make the disk the only selection whose actions live elsewhere.

### D7. Drop the 0.1.0 cleanup
- Remove the `{localappdata}\WinParted` `[UninstallDelete]` entry and its comment (and `UsedUserAreasWarning=no`, which only existed for it) from `installer/WinParted.iss`; remove the sentence from CONTRIBUTING.md. Archived OpenSpec changes stay as they are (history).

## Risks / Trade-offs

- [The flag was set on purpose (Windows storage policy, a cloned disk, forensic or backup tools)] → The confirmation says so; the action is never automatic and changes only the attribute, no data.
- [Windows clears the attribute but resets it at the next reconnect (some policies, some card readers)] → Out of scope; the app shows the state Windows reports after each refresh.
- [SD card hardware not available for testing] → The VHDX attached read-only (`Mount-DiskImage -Access ReadOnly`) exercises the same "stays read-only" path; the SD card check is optional.
- [The change on a real disk] → Never tested on the developer's physical disks: the spike and integration tests use VHDX files only; the end-to-end check uses a removable stick or card the user chooses.

## Migration Plan

None. Existing read-only disks simply gain the action. A 0.1.0 log folder, if any, is no longer removed by the uninstaller.
