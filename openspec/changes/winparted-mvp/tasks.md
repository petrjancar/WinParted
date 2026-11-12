# Tasks

## 1. Claude Design brief

- [x] 1.1 Write `docs/design/mvp-features.md`: product summary, target user, WinUI 3 / Fluent target, and the main window structure (disk list, layout bar, details panel, action area, notifications); verify every requirement in `specs/disk-inventory` maps to a described element
- [x] 1.2 Add to the brief every state (loading, error/retry, empty, busy, locked/protected, unhealthy, offline, read-only, uninitialized, unknown filesystem) with example content; verify against `specs/protected-storage` and `specs/device-change-detection`
- [x] 1.3 Add to the brief every dialog (Initialize, Online/Offline, Clear disk with type-number confirm and partition bar, Create partition, Delete, Format, Shrink, Extend, Change letter, Change label, close-while-busy, missing admin rights) with fields, defaults, validation and confirmation copy; verify each spec scenario involving a dialog is covered
- [x] 1.4 Add to the brief the full list of disabled-action reason texts and error messages (from D5/D7); verify each reason in the specs appears verbatim or with its final wording

## 2. Technical spikes

- [x] 2.1 Console spike: subscribe to `MSFT_StorageArrivalEvent`/`DepartureEvent`/`ModificationEvent` and plug/unplug a USB disk and attach/detach a VHDX on the Windows 11 development PC; record reliability and latency in `docs/spikes/device-events.md` and choose primary vs fallback per D9
- [x] 2.2 Console spike: run `MSFT_Volume.Format` and `MSFT_Partition.Resize` via `InvokeMethodAsync` on a VHDX and record whether progress is reported and how; document in `docs/spikes/smp-progress.md`
- [x] 2.3 Console spike: on an MBR VHDX, create partitions until SMP refuses and record its behavior (extended/logical or error) and the return codes for common failures (in use, read-only, not enough space); document in `docs/spikes/smp-behavior.md`
- [x] 2.4 Follow-up spike (`fullformat` command): full format on a fixed-size vs expandable VHDX, and removing a drive letter Windows assigns on its own; record results in `docs/spikes/smp-progress.md` and decide whether the Quick format switch stays

## 3. Repository and solution scaffold

- [x] 3.1 Add `LICENSE` (MIT), `.gitignore` (Visual Studio), `.editorconfig` and `README.md` with project description, MVP scope, limitations (no partition moving, NTFS/ReFS resize only) and build instructions, stating Windows 11 as the minimum supported OS; verify README build steps work on a clean clone
- [x] 3.2 Create `WinParted.sln` with `WinParted.App` (WinUI 3, unpackaged, self-contained, .NET 10), `WinParted.Core`, `WinParted.Storage`, `WinParted.Core.Tests`, `WinParted.Storage.IntegrationTests`; verify `dotnet build` succeeds
- [x] 3.3 Add app manifest with `requireAdministrator` and confirm the app launches elevated with an empty window on Windows 11 (confirmed with the published Release build)
- [ ] 3.4 Add GitHub Actions workflow building the solution and running `WinParted.Core.Tests` (integration category excluded); verify a green run

## 4. Core snapshot model

- [x] 4.1 Implement immutable snapshot types (disk, layout segment: partition or unallocated, volume, system facts) and stable identities per D4; verify with unit tests for equality and identity
- [x] 4.2 Implement unallocated region computation (ordered layout, ignore gaps under 1 MB) and binary size formatting per D10; verify unit tests for start, middle, end and no-free-space layouts
- [x] 4.3 Implement snapshot diffing to keep or move selection after refresh; verify unit tests for the `disk-inventory` "Manual refresh" scenarios
- [x] 4.4 Create reusable test snapshot builders for Windows 11 GPT default, legacy MBR, USB stick, dynamic disk, BitLocker volume and uninitialized disk

## 5. Protection classification

- [x] 5.1 Implement `ProtectionClassifier` per D6 with fail-closed behavior; verify unit tests for each row of the D6 table
- [x] 5.2 Verify one unit test per `protected-storage` scenario (EFI, Recovery, C: shrink allowed, pagefile, locked BitLocker, Storage Spaces, system disk clear, data partition on system disk, read-only disk)

## 6. Action availability and reasons

- [x] 6.1 Implement `ActionAvailability` with ordered rule evaluation and reason codes per D5; verify unit tests for each action kind on each selection type
- [x] 6.2 Implement operation-specific preconditions: resize only NTFS/ReFS, extend only into following space, MBR partition limit, FAT32 32 GB limit, label length per filesystem, initialize only on uninitialized disks; verify a unit test per `partition-operations` and `disk-operations` disabled scenario
- [x] 6.3 Add the English reason strings resource and verify every reason code resolves to a string (test enumerates all codes)

## 7. Storage: reading

- [x] 7.1 Implement `ReadSnapshotAsync` via CIM for disks, partitions and volumes (join partitions on `DiskId` = disk `Path`, map `FileSystem` "" to None, never call `GetSupportedSize` here); verify an integration test on an attached VHDX with two partitions returns the expected layout
- [x] 7.2 Gather system facts (Windows volume, boot/system flags, pagefile and dump volumes, BitLocker lock state, storage pool membership); verify an integration test that the running system disk is classified as protected
- [x] 7.3 Implement VHDX test fixture (diskpart create/attach/detach/delete, as proven by the spikes) with the guard that refuses non-fixture disks; verify a test that the guard rejects Disk 0

## 8. Operation runner

- [x] 8.1 Implement `OperationRunner` with a single slot, identity + fingerprint re-verification, busy state and post-operation refresh per D4/D7; verify unit tests with a fake provider (mismatch aborts, second operation rejected)
- [x] 8.2 Implement SMP status code to message mapping with "was anything changed" and raw details; verify unit tests for each mapped code from spike 2.3 and the unknown-code fallback

## 9. Storage: operations

- [x] 9.1 Implement Initialize, Online/Offline and Clear per D8; verify integration tests on VHDX for each `disk-operations` scenario that changes disk state
- [x] 9.2 Implement Create partition (with and without format, removing any drive letter Windows assigns when "No drive letter" was chosen) and Delete; verify integration tests including create-then-format failure leaving an unformatted partition and no stray letter
- [x] 9.3 Implement Format with the fixed file system list and `GetSupportedClusterSizes`, forwarding `WriteProgress` callbacks as progress; verify integration tests for NTFS, exFAT, FAT32 (32 GB or less) and quick vs full (full NTFS isn't possible on VHDX, so test full with exFAT/FAT32 and check that full NTFS on a virtual disk is disabled with its reason)
- [x] 9.4 Implement Shrink (with `GetSupportedSize` limits, queried on demand because it can take tens of seconds) and Extend; verify integration tests shrinking and extending an NTFS VHDX volume
- [x] 9.5 Implement drive letter add/change/remove and label change; verify integration tests for each

## 10. Device change detection

- [x] 10.1 Implement `IDeviceChangeSource` from SMP arrival/departure indications plus `Win32_VolumeChangeEvent` (spike 2.1) with the 500 ms debounce; verify a unit test that a burst becomes one refresh and an integration test that attaching a VHDX triggers a refresh within 3 seconds

## 11. UI: main window (after Claude Design delivers)

- [x] 11.1 Implement theme resources and shell from the delivered design (Mica, title bar, layout regions); verify visually against the design on Windows 11
- [x] 11.2 Implement disk list and layout bar control (proportional segments, minimum width, labels, fill indicator, lock badge, warning states); verify against the `disk-inventory` scenarios using test VHDX layouts
- [x] 11.3 Implement details panel and action area bound to `ActionAvailability`, with reason display for disabled actions; verify the Recovery-partition Extend reason appears on a Windows 11 default layout
- [x] 11.4 Implement loading/error/retry states, notifications, manual refresh and device-change handling in the UI (selection kept/cleared, toasts); verify `device-change-detection` scenarios by attaching/detaching a VHDX
- [x] 11.5 Implement the missing-admin-rights screen with "Restart as administrator"; verify by running unelevated from the debugger

## 12. UI: operation dialogs

- [x] 12.1 Implement shared confirmation and progress/result UI (busy indicator, success toast, error with expandable technical details, close-while-busy prompt); verify `operation-execution` scenarios with a fake provider
- [x] 12.2 Implement Initialize, Online/Offline and Clear disk dialogs (type-number confirm, partition bar with labels and used space, "Initialize now" or, for USB sticks left as empty MBR, "Create partition" follow-up); verify `disk-operations` scenarios on VHDX
- [x] 12.3 Implement Create partition and Format dialogs (size input, NTFS/exFAT/FAT32 options with disabled reasons, label, letter, quick format, "Don't format", the "programs will lose access" warning in Format); verify `partition-operations` create/format scenarios on VHDX
- [x] 12.4 Implement Delete (with the "programs will lose access" warning), Shrink (with the "calculating smallest size" loading state), Extend, Change letter and Change label dialogs; verify remaining `partition-operations` scenarios on VHDX (verified on the expendable USB stick: Format NTFS, Shrink, Extend, Change letter, Delete with D: offered again)
- [x] 12.5 Implement dialog self-closing when its target changes or disappears; verify by detaching the VHDX while a dialog is open (verified by unplugging the USB stick with a dialog open)

## 13. Integration checks

- [x] 13.1 Run the full manual QA checklist (every spec scenario) on the Windows 11 development PC using VHDX disks only; plus a full NTFS format on an expendable USB stick (not possible on VHDX); record results in `docs/qa/mvp-checklist.md` (done 2026-11-11: 77 of 79 scenarios OK; optional VHDX step with 2 scenarios skipped by decision)
- [x] 13.2 Verify the system disk of the development PC shows every protected item locked with the correct reasons and no destructive action is reachable
- [ ] 13.3 Publish a self-contained release build, verify it starts elevated (done on the development PC; the clean-machine test was dropped by decision), and attach it to a draft GitHub release (publish needs `EnableMsixTooling`, otherwise WinParted.pri is missing and the window fails to load)
