# Design

## Context

What Windows reports for a two-slot USB card reader with a card only in the microSD slot. This was read with `Get-Disk`, `Get-Volume`, `Get-Partition`, `MSFT_Disk` and `DriveInfo`, all read-only:

| | Empty SD slot | microSD card |
|---|---|---|
| `MSFT_Disk` | Disk 1, `Size` 0, `PartitionStyle` 0 (RAW), `IsOffline` true, `OfflineReason` 0, `HealthStatus` 0 (Healthy), `LogicalSectorSize` 0, `OperationalStatus` {53266 = 0xD012 "No Media"} | Disk 2, 62 GB, MBR, online |
| `MSFT_PhysicalDisk` | not listed | listed |
| `MSFT_Partition` | none | partition 1, E: |
| `MSFT_Volume` | `DriveLetter` D, `Size` 0, file system Unknown, no partition | E:, FAT32 |
| `DriveInfo` | D:, Removable, not ready | E:, Removable, ready |

How WinParted reads this today:
- `SnapshotReader` maps `MSFT_Disk` 1:1 to `Disk`. It never reads `OperationalStatus`.
- Used drive letters are `SystemFacts.OtherDriveLetters` (`DriveInfo` entries that aren't Fixed, Removable or NoRootDirectory) plus the partitions' letters. D: falls through both.
- `ActionAvailability.DiskActions` lists Initialize, Bring online and Clear disk for the slot, and only Bring online is allowed.
- `SmpStorageProvider.SetDriveLetter` removes the old letter, then calls `AddAccessPath`. A 42002 maps to `StorageErrorKind.Unknown`, which shows "Something went wrong".

## Goals / Non-Goals

**Goals:**
- Never offer a letter Windows already uses for a volume, including volumes without a partition.
- Show a drive without media as exactly that, with no actions that can't work.
- Turn 42002 into an accurate message, and keep "Nothing was changed" true for Change drive letter.

**Non-Goals:**
- Showing which drive letter an empty slot holds. Windows doesn't link the slot's volume to its disk: there's no partition, and `MSFT_Volume` has no disk reference. Matching on letters or device paths would be guesswork.
- Changing or removing the empty slot's letter. Without media there's nothing to act on, and Windows reassigns it anyway.
- Hiding empty slots. They are real devices, and hiding them would make a disk number vanish from the list without explanation.

## Decisions

### D1. A disk has no media when `OperationalStatus` contains 0xD012 or its size is 0

- `SnapshotReader` sets a new `Disk.HasNoMedia`.
- 0xD012 is the documented "No Media" value.
- Size 0 is a fallback for readers that report something else, such as "Not Ready". A 0-byte disk can't be used for anything, so treating it as no media is never wrong.
- Rejected: `MSFT_PhysicalDisk` absence. It's indirect and also true for some virtual disks.

### D2. Used letters include every `MSFT_Volume.DriveLetter`

- `StorageSnapshot.Facts.UsedDriveLetters` = `OtherDriveLetters` ∪ partition letters ∪ volume letters.
- Volumes come from the same SMP query `SnapshotReader` already runs, so there's no extra cost.
- Rejected: counting `DriveInfo` Removable drives that aren't ready. That relies on `DriveInfo`, which the existing comment notes can show a letter just after it was removed. SMP is the source the rest of the snapshot uses.

### D3. No media is checked first, in Core

**Availability:**
- `ReasonCode.NoMedia` comes before every other disk check in `CheckDisk`, including the read-only and protection locks.
- For a no-media disk, `DiskActions` lists Initialize and Clear disk (both disabled). It lists neither Bring online nor Take offline: the offline flag Windows reports is a side effect of having no media.
- Turn off read-only isn't listed either, even if the flag is set.

**Presentation (`Presenter.Disk`, `DiskDetails`):**
- `DiskState.NoMedia`.
- **Disk list:** a "No media" chip (neutral, the existing drive icon), meta "USB · No media", empty size text, no usage bar.
- **Header chips:** bus and "No media" only.
- **Details:** bus type, status "No media", serial number, location.
- These rules live in Core, so they get unit tests.

### D4. The window shows a No media panel

- `MainViewModel.IsNoMediaDisk` shows a card like the offline panel: icon, "No media.", "Insert a card or disk to use this drive." There's no button.
- The offline panel, the uninitialized panel and the layout bar are hidden for this state.
- No other window changes.

### D5. Change drive letter checks first and restores on failure

`SetDriveLetter(id, letter)`:
1. **Check before removing:** if `letter` is held by any volume other than this partition's (a fresh `MSFT_Volume` and `DriveInfo` read), fail with `DriveLetterInUse` before touching anything.
2. Remove the old letter, as today.
3. **If `AddAccessPath` fails:** re-read the partition.
   - If it now has `letter`, Windows assigned it first: return success.
   - Otherwise, if an old letter was removed, try to add it back. If that works, report the error with nothing changed. If it doesn't, report it as possibly changed (today's behavior).

Step 1 makes the race rare. Step 3 handles the rest without leaving a volume without its letter.

### D6. Error kinds

- **`StorageErrorKind.DriveLetterInUse`:** `Classify` maps 42002 to it. The message takes the letter when known: "Drive letter X: is already in use by another drive. Nothing was changed. Choose another letter." It isn't a may-have-changed kind.
- **`StorageErrorKind.CreatedButLetterFailed`:** `CreatePartition` wraps a failed letter step in it, like `CreatedButFormatFailed`:
  - With a format: "The partition was created and formatted, but drive letter D: is already in use by another drive. Use Change drive letter to give it a letter."
  - It's a may-have-changed kind, because the disk did change. Its "Was anything changed?" line says the partition was created.
- All texts go in `Texts.resx`.

### D7. Sample data

- Sample mode gains Disk 7: "Generic STORAGE DEVICE", USB, no media, holding I: (added to the sample's used letters).
- I: then isn't offered in sample dialogs, which lets the dialog side of D2 be checked without hardware.

### D8. Card insert and removal are found by checking, not by events

**Finding (task 6.1, `docs/spikes/smp-behavior.md`):** inserting or removing a card in a connected reader raises **no** event: not `MSFT_StorageArrivalEvent`, `MSFT_StorageDepartureEvent`, `MSFT_StorageModificationEvent`, `Win32_VolumeChangeEvent` or `Win32_DeviceChangeEvent`. The slot's volume (D:) exists with or without a card, so no volume arrives. Only `MSFT_Disk` changes: size, `OperationalStatus` and `IsOffline`, within about a second.

**Decision:** a `MediaPoller` (Core, `IDeviceChangeSource`) checks size, offline and no media every 2 seconds and raises `Changed` when they differ from the last applied snapshot. The existing debouncer then triggers one refresh, whose snapshot becomes the new baseline. Comparing against the snapshot (not the previous check) means a change right after a refresh is never missed, and an operation's own changes don't cause an extra refresh.
- **What is checked:** only the disks to watch, by number: disks without media and removable media disks (`Disk.HasNoMedia || Disk.IsRemovableMedia`), taken from each applied snapshot. A disk watched once stays watched while it's connected: in the end-to-end check, the refresh right after a card was inserted sometimes ran before Windows listed the card as removable, so the slot dropped out of the watch list and the card's removal went unnoticed. External hard drives usually report as fixed and aren't touched, so a sleeping drive isn't woken. With nothing to watch, nothing runs.
- **When it's paused:** while the window is minimized and while an operation is running. A change made meanwhile is found at the first check after the pause.
- **One check at a time:** a check that is still running (a slow or stuck device) makes the next ones skip. A failed check is ignored.
- **Storage side:** `SmpDeviceChangeSource` reads `Size`, `IsOffline` and `OperationalStatus` of the watched disk numbers with one WQL query (`WHERE Number = 1 OR Number = 2`).
- **Rejected:** listening for window messages (`WM_DEVICECHANGE` with the media flag). It needs hooks into the WinUI window, and `Win32_VolumeChangeEvent`, which is built on those messages, stayed silent here. Checking all disks: unnecessary, and could wake sleeping external drives.

### D9. Each connected disk is announced once

- **Finding (task 6.1):** plugging in the reader showed each "connected" notification twice. Two refreshes overlapped: a second device event arrived while the first refresh was still reading, and both compared against the same old snapshot.
- **Decision:** refreshes run one at a time (`MainViewModel`), so the second compares against the result of the first.
- A drive without media is announced as "USB disk connected: Generic MassStorageClass (no media)" instead of "(0 bytes)".
## Risks / Trade-offs

- **Other readers may report media absence differently.** The size-0 fallback covers "Not Ready" and similar. A reader that reports a non-zero size without media would still look like an offline disk, as today.
- **Volume letters from SMP could include a letter just freed.** It's the same source WinParted already trusts for partitions, so nothing gets worse. At worst a letter is skipped until the next refresh.
- **The check before removing (D5) costs one extra read** per Change drive letter. That's negligible.
- **Checking for media (D8) costs one small query every 2 seconds** while a removable drive or empty slot is connected. A card change shows within about 1 to 3 seconds instead of under one. Whether the query can wake a sleeping drive isn't tested; it's limited to removable media, which has nothing to spin up.
- **Restoring the old letter can itself fail** (another program took it). WinParted then reports "may have changed", which is the honest state.
