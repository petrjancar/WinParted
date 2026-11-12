# Design

## Context

Greenfield repository; nothing exists besides OpenSpec scaffolding. See `proposal.md` for motivation and scope and `specs/` for behavior.

Hard constraints that shape the design:

- **SMP is the only storage backend.** All disk, partition and volume data and modifications go through the Windows Storage Management API (CIM classes `MSFT_Disk`, `MSFT_Partition`, `MSFT_Volume` in `root\Microsoft\Windows\Storage`). SMP cannot move partitions, can only resize NTFS/ReFS, and can only extend into contiguous space after a partition. The product accepts these limits and explains them instead of working around them.
- **Modifications need administrator rights.**
- **The UI design is produced externally by Claude Design** from a features document we write. Implementation of the visual layer must follow that design, so the design brief comes first and the non-visual layers must not depend on it.
- **Mistakes destroy user data.** Correctness of protection rules and target identity matters more than features.

## Goals / Non-Goals

**Goals:**
- A layered codebase where all availability and protection logic is testable without touching real disks.
- One central place that decides whether an action is allowed and produces its user-facing reason.
- Integration tests that exercise every operation against real SMP, using throwaway virtual disks.
- A design brief detailed enough that Claude Design can produce every screen, state and dialog without guessing.

**Non-Goals:**
- Supporting storage backends other than SMP (no direct IOCTLs, no diskpart scripting, no VDS).
- Cancelling an operation once SMP has started it.
- Localization beyond English (strings are still kept in resources so translation can be added later).
- Installer, auto-update and code signing (distribution is a zipped self-contained build for MVP).

## Decisions

### D1. WinUI 3, unpackaged, self-contained, `requireAdministrator`
The app targets Windows 11 only. It is a WinUI 3 (Windows App SDK) desktop app on .NET 10 (LTS), published unpackaged and self-contained, with an app manifest requesting `requireAdministrator`.
- *Why:* native Windows 11 Fluent look (Mica, standard controls) matches what Claude Design will produce; unpackaged avoids MSIX problems with elevation; one elevated process avoids building an IPC broker.
- *Alternatives:* WPF with Fluent theme (more mature, weaker native look; rejected by user preference). Unelevated UI plus elevated helper process (better least-privilege, but doubles the architecture for little MVP benefit). MSIX packaging (elevation and full-trust complications).
- The "missing rights" screen (`app-privileges` spec) covers the case where the manifest is bypassed, for example when launched under a debugger without elevation.

### D2. Three-layer solution

```
 WinParted.App        WinUI 3 views + view models (CommunityToolkit.Mvvm)
       |                   - binds to snapshot + ActionState
       v
 WinParted.Core       pure .NET, no Windows APIs
       |                   - immutable snapshot model
       |                   - ProtectionClassifier
       |                   - ActionAvailability (enabled? + reason text)
       |                   - operation requests, error-message mapping
       v
 WinParted.Storage    SMP via Microsoft.Management.Infrastructure (CIM)
                          - IStorageProvider: ReadSnapshot(), Execute(op)
                          - IDeviceChangeSource
                          - system facts: Windows volume, pagefile, BitLocker
```

- *Why:* Core holds every rule that can destroy data if wrong and is unit-testable with hand-built snapshots. Storage is a thin, mechanical translation to CIM. App holds only presentation and can be rebuilt freely when the design arrives.
- *Alternative:* calling CIM from view models (fast to start, untestable, rejected).

### D3. Immutable full snapshots; always reload everything
`IStorageProvider.ReadSnapshotAsync()` returns an immutable `StorageSnapshot` (disks -> ordered layout of partitions and unallocated regions -> volumes, plus system facts). Every refresh (manual, device change, after an operation) reads a complete new snapshot. The UI diffs by stable identity to keep selection.
- *Why:* incremental patching from events can drift; a full read is cheap at desktop scale (a few disks, tens of partitions).
- Unallocated regions are computed in Core from partition offsets and disk size (ignoring gaps under 1 MB, which come from alignment).

### D4. Stable identity, never disk number
Disks are identified by `MSFT_Disk.ObjectId` (with `UniqueId` and `Path` as a cross-check), partitions by disk identity + `Offset` (+ GPT `Guid` when present), volumes by `UniqueId` (`\\?\Volume{GUID}\`). Disk numbers are display-only.
- Every operation request carries the identity and a fingerprint of the properties it depends on (size, offset, partition count, file system). The runner re-reads the target right before executing and aborts on mismatch (`operation-execution` "Target re-verification").

### D5. Central `ActionAvailability` with reason codes
A pure function `(snapshot, selection, isBusy) -> IReadOnlyList<ActionState>` where `ActionState = (ActionKind, IsEnabled, ReasonCode?, ReasonArgs)`. Reason codes map to localized plain-language strings in one resource file. Rules are evaluated in a fixed order: busy -> read-only disk -> managed disk (dynamic/Spaces) -> protected item -> operation-specific preconditions (filesystem, contiguous space, MBR limit, size limits).
- *Why:* the spec requires every disabled action to explain itself; scattering checks through view models would produce inconsistent or missing reasons.
- The same function is called by the runner before executing, so the UI and execution can never disagree.

### D6. Protection classification
`ProtectionClassifier` (Core) marks items using facts gathered by Storage:

| Item | Detection |
|---|---|
| EFI / MSR / Recovery | GPT type GUIDs `c12a7328-...`, `e3c9e316-...`, `de94bba4-...`; MBR type `0x27`; `MSFT_Partition.IsSystem` |
| Boot partition, Windows volume | `MSFT_Partition.IsBoot`; volume containing `%SystemRoot%` |
| System disk | disk containing any of the above |
| Pagefile / crash dump | `Win32_PageFileUsage` plus dedicated dump file setting in the registry |
| Locked BitLocker | `Win32_EncryptableVolume.GetLockStatus` (`root\CIMV2\Security\MicrosoftVolumeEncryption`) |
| Dynamic disk | LDM partition types (GPT `5808c8aa-...` / `af9b60a0-...`, MBR `0x42`) |
| Storage Spaces | pooled physical disks are normally hidden from `MSFT_Disk`; a visible disk whose `MSFT_PhysicalDisk.CannotPoolReason` contains 2 ("In a pool") is marked managed |
| Read-only | `MSFT_Disk.IsReadOnly` |

If a fact cannot be determined (for example the BitLocker query fails), the classifier fails closed: the item is treated as protected with a generic reason.

### D7. Operation runner: single slot, async CIM, progress, error mapping
- One `OperationRunner` with a single concurrency slot; `IsBusy` feeds D5.
- Operations call SMP methods through `CimSession.InvokeMethodAsync` on a background thread. Format reports progress through `CimOperationOptions.WriteProgress` callbacks (percent, no time estimate; smooth steps about every 2.5 s during a full format on a real disk), which are forwarded to the UI. A callback reaching 100% is not success: only the return value counts. Resize and all other methods report nothing, so the UI shows indeterminate progress. `RunAsJob` is not used (unsupported on Resize, unhelpful on Format). See `docs/spikes/smp-progress.md`.
- SMP methods return numeric status codes plus `ExtendedStatus`. A mapping table in Core turns known codes (observed in `docs/spikes/smp-behavior.md`: 1 not supported, 5 invalid parameter, 4097 size not supported, 40000 not enough capacity, 41001 already initialized, 41002 disk read-only; plus access denied) into plain-language messages stating whether anything changed. Unknown codes produce a generic message. Raw code and text are always kept for the "technical details" expander.
- After every operation, success or failure, a full snapshot refresh runs.
- SMP does not protect anything itself: it deletes the MSR partition, creates partitions on offline disks, reports success when deleting an already deleted partition, and dismounts volumes that programs are using instead of failing. Protection (D5/D6), re-verification (D4) and the "programs will lose access" warning in Format/Delete confirmations are therefore WinParted's job.
- Closing the window while busy prompts. Because the SMP call runs inside the Windows storage provider, an operation already started is not aborted by the app exiting; the prompt says so.

### D8. Operation mapping to SMP

| Operation | SMP call(s) |
|---|---|
| Initialize | `MSFT_Disk.Initialize(PartitionStyle)` |
| Online / Offline | `MSFT_Disk.Online()` / `Offline()` |
| Clear | `MSFT_Disk.Clear(RemoveData=true, RemoveOEM=true, ZeroOutEntireDisk=false)`; on removable media Windows keeps an empty MBR table, so the follow-up action depends on the resulting state |
| Create partition | `MSFT_Disk.CreatePartition(Size/Offset, AssignDriveLetter, ...)`, then `MSFT_Volume.Format` if requested; if "No drive letter" was chosen and Windows assigned one anyway, remove it with `RemoveAccessPath` |
| Delete | `MSFT_Partition.DeleteObject()` |
| Format | `MSFT_Volume.Format(FileSystem, FileSystemLabel, AllocationUnitSize, Full)` |
| Shrink / Extend | `MSFT_Partition.GetSupportedSize()` for limits (slow: 22 s on a 476 GB system volume, so only when the dialog opens, never in a snapshot read), then `MSFT_Partition.Resize(Size)` |
| Drive letter | `MSFT_Partition.AddAccessPath` / `RemoveAccessPath` |
| Label | `MSFT_Volume.SetFileSystemLabel` |
| File system options | Fixed list NTFS, exFAT, FAT32 (`GetSupportedFileSystems` proved unreliable: it returned only NTFS for a volume that then formatted fine as FAT32 and exFAT); cluster sizes from `GetSupportedClusterSizes` |

Partitions are joined to their disk on `MSFT_Partition.DiskId` = `MSFT_Disk.Path` (not `ObjectId`). Unformatted volumes report `FileSystem = ""`. On MBR disks SMP creates an extended partition automatically when the fourth partition is requested; Core's layout and MBR rules already match this.

Create partition with format is two SMP calls. If formatting fails after the partition was created, the error says so and the new partition is left in place (shown unformatted) rather than silently deleted.

### D9. Device change detection
- Disk arrival and removal: SMP CIM indications (`MSFT_StorageArrivalEvent`, `MSFT_StorageDepartureEvent`) via `CimSession.SubscribeAsync`. The spike showed them immediate and reliable (`docs/spikes/device-events.md`).
- Volume arrival and removal: `Win32_VolumeChangeEvent`, because SMP sends no events for partition or volume changes, including those made by other tools.
- Changes that add or remove no volume (e.g. another tool resizing a partition) are not detected; manual refresh covers them. `CM_Register_Notification` is not needed.
- All events go through a 500 ms debounce, then a full snapshot reload (D3). Open dialogs hold the target identity and fingerprint; after each reload the dialog checks them and closes itself with an explanation on mismatch.

### D10. Size units
Sizes use binary multiples with the labels and the three-significant-digit format Windows Explorer uses ("GB" = 1024^3 bytes, e.g. "57.3 GB"), so numbers match what users see elsewhere in Windows. Partition creation and resize are aligned to 1 MB; the UI rounds displayed limits accordingly.

### D11. Design brief before UI
`docs/design/mvp-features.md` is written first. It lists every screen, panel, dialog and state (loading, empty, error, busy, locked, unhealthy, offline, uninitialized), every action with its preconditions and reason texts, and the confirmation copy for destructive actions, derived from the specs. It names WinUI 3 / Fluent as the target so the design uses components that exist. App layout work waits for the design; Core and Storage do not.

### D12. Testing strategy
- **Core unit tests**: snapshot builders for typical layouts (Windows 11 default GPT, legacy MBR with system-reserved partition, USB stick, dynamic disk, BitLocker, uninitialized). Every reason code has at least one test.
- **Full format coverage**: SMP refuses a full NTFS format on virtual disks (both fixed and expandable VHDX), but does full NTFS, exFAT and FAT32 on a real USB disk (`docs/spikes/smp-progress.md`). The Format dialog therefore offers quick and full for every file system and disables only full NTFS on virtual disks, with a reason. VHDX integration tests cover full exFAT/FAT32; full NTFS is in the manual QA checklist on a USB stick.
- **Storage integration tests**: each test creates a VHDX in a temp folder with diskpart (`create vdisk` + `attach vdisk`, no Hyper-V required; the approach the spikes proved), runs real SMP operations against it, and detaches and deletes it. A guard refuses to operate on any disk whose location does not match the test's VHDX path. Tests need elevation and are in a separate category so they never run accidentally on CI agents without isolation.
- **Manual QA** on the Windows 11 development PC using the same VHDX approach (never the PC's real data disks), following a checklist derived from the specs.

## Risks / Trade-offs

- [A wrong protection rule lets a user delete a boot partition] -> Classifier fails closed, combines several signals per item, and has unit tests for each known Windows layout.
- [Target changes between confirm and execute] -> Identity + fingerprint re-verification immediately before execution (D4).
- [SMP behavior differs between Windows 11 editions or builds, and all testing happens on one Windows 11 Pro PC] -> Cluster sizes are queried from SMP; format options are limited to NTFS, exFAT and FAT32, which every edition supports. ReFS formatting is left out of the MVP because Windows 11 Pro only creates ReFS as a Dev Drive.
- [Windows 10 users try the app] -> Windows 10 is unsupported and untested; the README states Windows 11 as the minimum. The app does not actively block Windows 10 in the MVP.
- [External partition changes that add or remove no volume go unnoticed] -> Manual refresh (F5); acceptable for the MVP.
- [Format and Delete disconnect programs using the volume] -> The confirmation warns about it (spec operation-execution, "Programs using the volume").
- [Long operations cannot be cancelled] -> The confirmation warns about duration for full format and shrink; closing during an operation is explained honestly.
- [Users expect GParted-style partition moving] -> Reasons and the README state the limitation explicitly; it is v2 scope.
- [WinUI 3 rough edges when running elevated] -> The MVP doesn't use features that break when elevated (drag and drop, packaged-only APIs); verified in the scaffold task.
- [Create+format is two calls and can half-fail] -> Explicit error and visible unformatted partition (D8), never silent cleanup.

## Migration Plan

Not applicable (new application). Rollback is uninstalling (deleting the folder).

## Distribution and updates (decided)

- **MVP:** a portable zip of the self-contained build (English only), attached to a GitHub release. Updates are manual: users download the new zip from GitHub Releases and replace the folder. Nothing in the app checks for updates.
- **Next version:** an installer (Inno Setup: Program Files, Start menu shortcut, uninstaller) next to the zip, and an in-app update notice. The app asks GitHub for the latest release, shows a non-blocking "WinParted x.y is available" notification with a Download link, and can be turned off. The user installs it; no automatic installs, and never during an operation.
- **Not planned:** single-file exe (native WinUI parts would be unpacked on every start), MSIX/Microsoft Store (packaged apps can't simply require administrator rights), winget (maybe later), automatic installation of updates.

## Open Questions

- Code signing (SmartScreen warns about unsigned apps that ask for administrator rights). This doesn't affect MVP specs or tasks.
