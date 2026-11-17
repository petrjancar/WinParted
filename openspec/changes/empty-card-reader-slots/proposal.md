# Proposal

## Why

Two bugs showed up with a USB card reader that has two slots (SD and microSD) and one card inserted:

1. **Create partition failed with "Something went wrong" (code 42002).** Windows gives the *empty* slot a drive letter (D:), even with no card in it. WinParted builds its list of used letters from partitions and from non-disk drives (network, optical). The empty slot has a volume but no partition, so its letter is in neither list. WinParted offered D:, and Windows refused it with "the requested access path is already in use". The partition was created and formatted, but it got no letter, and the error message didn't say any of that.
2. **The empty slot shows as a broken disk.** Windows lists it as a 0-byte disk with no partition table, reported as offline, health OK, and status "No Media" (`OperationalStatus` 0xD012). WinParted shows it as an offline, uninitialized, healthy 0-byte disk with a 0-byte sector size, and offers **Bring online** as the main action, which can't work without a card.

Both affect anyone with a multi-slot card reader, which is a common setup.

## What Changes

- **Drive letters held by volumes without a partition count as used.** Letters come from every volume Windows reports, not only from partitions. The empty slot's letter is then never offered in Create partition or Change drive letter.
- **Clear message when a letter is taken anyway** (for example, another program took it between the dialog and the operation): Windows' code 42002 gets its own plain-language message instead of "Something went wrong".
  - Create partition: "The partition was created and formatted, but drive letter D: is already in use by another drive. Use Change drive letter to give it a letter."
  - Change drive letter: it checks that the letter is still free before removing the old one, and puts the old letter back if adding the new one fails. That way "Nothing was changed" stays true.
  - If the partition already has the requested letter when Windows reports the clash (Windows assigned it on its own first), the operation counts as a success.
- **A drive without media is shown as such.** A disk Windows reports as "No Media" gets a **No media** state:
  - a "No media" badge in the disk list and header instead of Offline, Not initialized, Healthy and a size;
  - in place of the layout and offline panel, "No media. Insert a card or disk to use this drive.";
  - only the details that mean something (bus type, status "No media", serial number, location);
  - every disk action unavailable with the reason "There's no card or disk in this drive. Insert one first."
- **Inserting a card** into a slot that's shown as No media updates the disk within the usual 3 seconds. Removing it returns the slot to No media.
- **Sample data** gains an empty card reader slot that holds a drive letter, so the state can be designed and checked in sample mode.
- Ships as version **1.0.1**.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `disk-inventory`: new requirement "Drive without media" (how an empty slot is listed, its details and that it updates when a card is inserted or removed).
- `operation-execution`: "Unavailable actions explain why" gains the no-media reason; "Operation results" gains the drive-letter-in-use outcome.
- `partition-operations`: "Change drive letter" and "Format options on create" define a free letter as one no volume holds, including empty card reader slots, and say what happens when the letter is taken anyway.

## Impact

- **Core:**
  - `Disk.HasNoMedia`.
  - a `DiskState.NoMedia` presentation, with chips, details and the panel text;
  - a `ReasonCode.NoMedia` rule ahead of all disk actions;
  - `StorageErrorKind.DriveLetterInUse` (42002) and a "created, but no letter" kind;
  - texts in `Texts.resx`;
  - a sample disk;
  - unit tests.
- **Storage:**
  - `SnapshotReader`: reads `OperationalStatus` and takes used letters from `MSFT_Volume.DriveLetter`;
  - `SmpStorageProvider.SetDriveLetter`: checks before removing, restores the old letter, treats the letter as success when it's already set.
- **App:** the main area shows the No media panel; nothing else in the window changes.
- **Docs:**
  - design brief sections 3.2, 4.2, 7.1, 8.1 and 10;
  - `docs/spikes/smp-behavior.md`: what Windows reports for an empty slot;
  - README only if needed;
  - the Claude Design canvas: the No media state.
- **Not changed:** what the operations do on a disk that has media.
