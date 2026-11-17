# Tasks

## 1. Record the findings

- [ ] 1.1 Add a section "Drive without media" to `docs/spikes/smp-behavior.md` with the table from design.md (Context) and the 42002 meaning ("the requested access path is already in use"); verify the section matches what the read-only queries showed

## 2. Core

- [ ] 2.1 Add `Disk.HasNoMedia`, `DiskState.NoMedia` and `ReasonCode.NoMedia` (checked first for disk actions; Initialize and Clear disk listed, no Bring online, Take offline or Turn off read-only) with texts; verify unit tests for the `Drive without media` scenario in operation-execution and that a read-only, offline or managed no-media disk still gets the no-media reason
- [ ] 2.2 Present the No media state (D3): list chip, meta, no size or usage bar, header chips, details rows; verify `PresenterTests` for the `Empty card reader slot` and `Empty slot selected` scenarios
- [ ] 2.3 Add `StorageErrorKind.DriveLetterInUse` (42002) and `CreatedButLetterFailed` with their texts (D6); verify `StorageErrorMapper` tests and the dialog texts for both
- [ ] 2.4 Add sample Disk 7 (USB, no media, holding I:) per D7; verify a sample-snapshot test that I: isn't among the free letters

## 3. Storage

- [ ] 3.1 `SnapshotReader`: set `HasNoMedia` from `OperationalStatus` 0xD012 or size 0 (D1), and add `MSFT_Volume` letters to the used letters (D2); verify with the reader connected (card in one slot only) that a debug or sample-free run shows the empty slot as No media and that Create partition on the card doesn't offer the slot's letter
- [ ] 3.2 `SetDriveLetter`: check before removing, success when the letter is already set, restore the old letter on failure (D5); `CreatePartition` wraps a failed letter step in `CreatedButLetterFailed`; verify with integration tests on a VHDX (Terminal (Admin), developer): change to a letter that's in use by a second test VHDX fails with nothing changed and the old letter kept

## 4. App

- [ ] 4.1 Write the wording first in `docs/design/mvp-features.md`: 3.2 (list), 4.2 (No media state), 7.1 (reason), 8.1 (letter-in-use and created-but-no-letter errors), 10 (sample disk); verify every scenario of this change with visible text maps to the brief
- [ ] 4.2 Show the No media panel and hide the layout, offline and uninitialized panels for that state (D4); verify in sample mode on Disk 7 in light and dark, with Tab and a screen reader name, and that the details and actions match 2.1 and 2.2
- [ ] 4.3 Add the No media state to the Claude Design canvas (disk states board); verify the canvas and the brief agree

## 5. Release

- [ ] 5.1 Set the version to 1.0.1 in `Directory.Build.props`, and update README.md only if needed; verify against the docs rule in AGENTS.md (no screenshot change: the main window's default view is unchanged)

## 6. End-to-end check (developer)

- [ ] 6.1 With the card reader (card in the microSD slot only) and an elevated Release build: the empty slot shows No media with no allowed actions; Create partition on the card doesn't offer the slot's letter and assigns the chosen letter; inserting a card into the empty slot shows it within 3 seconds and removing it returns to No media (if no refresh happens, add the `MSFT_StorageModificationEvent` subscription per D8 and repeat); verify all pass
