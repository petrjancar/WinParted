# WinParted MVP – QA checklist

Every scenario in `openspec/changes/winparted-mvp/specs/` with how it was checked (task 13.1).
Machine: the Windows 11 development PC. Real-disk checks used only temporary VHDX disks and an empty
Kingston DataTraveler 3.0 USB stick (14.4 GB), never the PC's own disks.

**Evidence**

- **Real**: done on real storage:
  - *USB*: the Kingston stick, in the app.
  - *VHDX*: `WinParted.Storage.IntegrationTests` (16 tests, run elevated, all passed).
  - *Spike*: `spikes/WinParted.Spikes`, results in `docs/spikes/`.
  - *Release*: the published Release build on this PC.
- **Unit**: `WinParted.Core.Tests` (245 tests), test name given.
- **Sample**: seen in the app in sample mode (`--sample`) by screenshot or by driving it with key presses.

**Status**

- **OK**: checked on real storage, or a pure UI/logic behaviour checked by unit tests and in the app.
- **OK (no real repro)**: the situation can't be produced safely or at all on this PC. The logic is unit-tested and the UI was seen in sample mode.
- **NOT RUN**: an optional check that was skipped by decision.

## app-privileges

| Scenario | Evidence | Status |
|---|---|---|
| User approves elevation | Release: UAC prompt shown, app opens with all features | OK |
| User declines elevation | Windows' own behaviour (`requireAdministrator` manifest, verified in the published exe); manual step 1: nothing opens | OK |
| Launched without rights | Sample: Debug build (asInvoker) shows "needs administrator rights" + Restart as administrator | OK |

## device-change-detection

| Scenario | Evidence | Status |
|---|---|---|
| USB stick connected | VHDX: `Attaching_a_vhdx_triggers_a_refresh`; USB: stick appears after plugging in; manual step 2: listed with notification within 3 s | OK |
| USB stick removed | Unit: `SelectionReconcilerTests`; USB: unplug handled; manual step 2: Disk 0 stayed selected | OK |
| Multiple notifications for one plug-in | Unit: `Burst_becomes_one_refresh`, `Separate_events_refresh_separately` | OK |
| Selected disk unplugged | USB: first remaining disk selected with notification (fixed after the first report, then rechecked); Unit: `Removed_disk_clears_selection_and_reports_it` | OK |
| Last disk unplugged | Unit: `Empty_layout`, `Removed_disk_clears_selection_and_reports_it` (a PC always keeps its system disk) | OK (no real repro) |
| Disk changes while confirm dialog is open | USB: unplugged with a dialog open → dialog closed, "Nothing was modified"; Unit: `Dialog_detects_a_changed_target` | OK |
| Unrelated disk changes while dialog is open | Unit: `Dialog_detects_a_changed_target` (unchanged target isn't stale); manual step 3: Change label on C: stayed open | OK |
| Other disk connected during format | — | NOT RUN (optional step 7 skipped) |

## disk-inventory

| Scenario | Evidence | Status |
|---|---|---|
| Disks shown at startup | USB + Release: NVMe system disk and USB stick listed with all fields | OK |
| Uninitialized disk | VHDX: `Initialize_offline_online_and_clear`; Sample: Disk 3 "Not initialized" | OK |
| Unhealthy disk | Unit: `Disk_entries_carry_status_chips` | OK (no real repro) |
| Mixed layout | Release: system disk EFI · MSR · C: · Recovery; VHDX: `Snapshot_layout_covers_the_whole_disk_in_order`; Unit: `Windows11_layout_has_four_partitions_then_free_space` | OK |
| Tiny partition | Unit: `Tiny_partitions_get_the_minimum_and_bar_is_marked_not_to_scale`; Release: 16 MB MSR visible and selectable | OK |
| Data volume | Unit: `Windows11_segments_match_the_design`; USB: "FLASH (D:)" segment with used space | OK |
| Unrecognized filesystem | Unit: `Unknown_and_unformatted_segments`, `Reported_name_is_used_for_unknown_file_systems`; Sample: Linux partition on Disk 1 | OK (no ext4 disk at hand) |
| Select a partition | Unit: `Partition_details`; USB + Sample: details panel | OK |
| Select unallocated space | Unit: `Unallocated_and_disk_details`; USB: free space after Delete/Shrink | OK |
| Refresh keeps selection | Unit: `Refresh_keeps_selected_partition` | OK |
| Selected item gone after refresh | Unit: `Missing_partition_falls_back_to_its_disk`, `Unallocated_selection_falls_back_to_disk_when_region_is_gone` | OK |
| Storage service unavailable | Error state with "Try again" built per design; can't break the storage service safely | OK (no real repro) |

## disk-operations

| Scenario | Evidence | Status |
|---|---|---|
| Initialize new disk as GPT | VHDX: `Initialize_offline_online_and_clear` (16 MB MSR + free space); in the app: optional VHDX step | OK |
| MBR on a disk larger than 2 TB | Unit: `MBR_on_a_disk_over_2_TB_warns` | OK (no >2 TB disk) |
| Initialize not offered for initialized disk | Unit: `Initialize_not_offered_for_initialized_disk`; USB + Sample: reason points to Clear disk | OK |
| Take disk offline | VHDX: `Initialize_offline_online_and_clear` | OK |
| Bring disk online | VHDX: `Initialize_offline_online_and_clear`; Sample: offline Disk 4 | OK |
| Removable media can't go offline | USB: Windows refused (code 1), now disabled with reason; Unit: `Removable_media_cant_go_offline` | OK |
| Offline disk details | Sample: Disk 4 (offline reason "Policy", Bring online primary); Unit: `Offline_disk_blocks_initialize_and_clear` | OK |
| Clear a USB stick | Spike `usb`: stick left as empty MBR; VHDX: clear → not initialized | OK |
| Wrong number typed | Unit: `Clear_dialog_requires_the_exact_disk_number`; Sample: wrong number + Enter does nothing | OK |
| Confirmation shows what will be lost | Unit: `Clear_dialog_requires_the_exact_disk_number`; USB: dialog with "D: … 44.2 MB used" | OK |
| Offer after clear | Not reachable in sample mode (Clear is refused there); the stick stays MBR, so it needs a non-removable disk | NOT RUN (optional step 7 skipped; same code path as the USB variant) |
| Offer after clearing a USB stick | Spike `usb` (empty MBR); "Create partition" follow-up; manual step 4: Create partition offered | OK |

## operation-execution

| Scenario | Evidence | Status |
|---|---|---|
| Unallocated region selected | Unit: `Unallocated_selection_offers_only_create`, `Unallocated_offers_create_in_its_own_group` | OK |
| Extend blocked by following partition | Unit: `Extend_blocked_by_following_partition`; Release: C: on the system disk | OK |
| Shrink on unsupported filesystem | Unit: `Shrink_on_unsupported_filesystem`; USB: FAT32 "FLASH (D:)" | OK |
| Delete confirmation names the data | Unit: `Delete_dialog_texts`; USB: Delete | OK |
| Programs using the volume | Unit: `Delete_dialog_texts`; Spike: Windows dismounts volumes in use | OK |
| Cancel does nothing | Unit: `Confirmed_operation_runs_and_refreshes` (only confirm runs); Sample: Enter in Delete cancels | OK |
| Same keyboard behaviour in every dialog | Sample, driven by key presses: Clear (wrong/right number), Create (typed size), Delete | OK |
| Confirmed format runs at once | USB: Format NTFS; VHDX: `Quick_format_each_file_system` | OK |
| Disk number reassigned | Unit: `Disk_number_reassigned`, `Same_disk_with_new_number_is_also_aborted` | OK (no real repro) |
| Second operation blocked | Unit: `Second_operation_is_rejected_while_one_runs`, `Busy_disables_everything_first` | OK |
| Long full format | VHDX: `Full_format_reports_progress_…`; Spike `fullformat`/`usb`: progress on the stick (NTFS, exFAT, FAT32); Sample: busy bar; manual step 5: progress shown, window usable | OK |
| Not enough space | VHDX: `Resize_below_minimum_is_reported_plainly`; Unit: `Extended_status_message_goes_to_details` | OK |
| Disk removed mid-operation | Unit: `Disk_removed_mid_operation`, `Failure_on_a_disk_still_present_keeps_the_original_error`; manual step 6: disconnected message shown | OK |
| Close attempt during shrink | Sample: close-while-busy dialog; manual step 5: warning during a full format, Keep running | OK |

## partition-operations

| Scenario | Evidence | Status |
|---|---|---|
| Create using all free space | USB: Create with Maximum; Unit: `Create_dialog_defaults_and_size_validation` | OK |
| Size limits are exact | Unit: `Size_limits_are_exact`; USB: "14.43 GB" limit and Maximum | OK |
| Create a smaller partition | USB: smaller size, rest stays unallocated; Sample: 100 GB of 365 GB | OK |
| MBR partition limit reached | Unit: `MBR_partition_limit_reached`, `MBR_with_three_partitions_can_create` | OK |
| Create without formatting | VHDX: `Create_without_format_has_no_letter_and_delete_frees_space`; Unit: `Create_without_format_has_no_letter` | OK |
| FAT32 on a 64 GB partition | Unit: `FAT32_on_a_64_GB_partition`, `FAT32_allowed_up_to_32_GB` | OK |
| No ReFS option | Unit: `No_ReFS_option`; USB + Sample: NTFS, exFAT, FAT32 only | OK |
| Delete a data partition | USB: Delete, and D: is offered again afterwards; VHDX: delete frees space | OK |
| Delete a Linux partition | Unit: `Delete_dialog_texts`, `Linux_partition_can_only_be_deleted` | OK (no Linux disk) |
| Quick format as exFAT | VHDX: `Quick_format_each_file_system`; USB | OK |
| Full format explained | Sample: Format dialog with Quick format off | OK |
| Quick and full for every file system | Spike `usb`: full NTFS/exFAT/FAT32 on the stick; Unit: `FormatChoices.FullFormatReason` | OK |
| Full NTFS on a virtual disk | VHDX: `Full_format_reports_progress_and_full_ntfs_on_vhdx_is_refused`; Unit: `Format_dialog_defaults` | OK |
| Shrink to free space | USB: Shrink (with Smallest); VHDX: `Shrink_and_extend_ntfs` | OK |
| Minimum size being calculated | Sample: "calculating" state; Spike: `GetSupportedSize` takes 22 s on the system disk | OK |
| Minimum size explained | Unit: `Shrink_dialog_validation_and_explanation` | OK |
| Extend into following free space | USB: Extend (with Maximum); VHDX: `Shrink_and_extend_ntfs`; Unit | OK |
| Free space before the volume | Unit: `Free_space_before_the_volume` | OK |
| Change letter | USB; VHDX: `Drive_letter_add_change_remove_and_label` | OK |
| Remove letter | VHDX: `Drive_letter_add_change_remove_and_label`; Unit: `Letter_dialog_warnings` | OK |
| Rename volume | VHDX: `Drive_letter_add_change_remove_and_label`; USB: "FLASH" label | OK |
| Label too long for FAT32 | Unit: `Label_too_long_for_FAT32`, `Label_dialog_validation` | OK |

## protected-storage

| Scenario | Evidence | Status |
|---|---|---|
| EFI partition shown | Release: system disk EFI visible and locked; VHDX: `Efi_and_reserved_partitions_are_locked` | OK |
| Delete Recovery partition | Release: Recovery locked; Unit: `Delete_Recovery_partition` | OK |
| Shrink C: | Release: C: Format/Delete disabled, Shrink allowed; Unit: `Shrink_C` | OK |
| Pagefile on D: | Unit: `Pagefile_on_D`, `Unknown_pagefile_state_fails_closed` | OK (no pagefile on a data volume) |
| Locked encrypted USB drive | Unit: `Locked_encrypted_USB_drive`; Sample: Disk 5 "Vault" | OK (no BitLocker stick) |
| Storage Spaces member | Unit: `Storage_Spaces_member`, `Unknown_storage_spaces_membership_fails_closed` | OK (no pool) |
| Clear system disk | Release: disabled with "Windows is installed on this disk"; VHDX: `Running_system_disk_is_classified_as_protected` | OK |
| Data partition on system disk | Unit: `Data_partition_on_system_disk` | OK (system disk has no data partition) |
| Write-protected SD card | Unit: `Write_protected_SD_card` | OK (no write-protected card) |
| Offline and read-only disk | Unit: `Offline_and_read_only_disk` | OK (no real repro) |
| No advanced unlock | Unit: `No_advanced_unlock`; Release: no override anywhere in the UI | OK |

## Manual checks (run 2026-11-11, all passed except the skipped optional step 7)

Run the **Release build** (`artifacts\WinParted-win-x64\WinParted.exe`) and use only the **USB stick**.

1. **Decline elevation.** Start WinParted and click **No** on the prompt. Nothing opens.
2. **Connect and remove.** Select Disk 0, then plug in the stick. Within about 3 seconds it's listed and a "connected" notification appears. Unplug it. It disappears and Disk 0 stays selected.
3. **Unrelated change with a dialog open.** Unplug the stick, open **Change label** on C:, then plug the stick in. The dialog stays open and the list updates behind it. Cancel.
4. **Clear the stick.** Use Clear disk and type its number. The result is an empty MBR disk, and the notification offers **Create partition**.
5. **Full format.** Create a partition, then Format it as NTFS with **Quick format off**. While it runs:
   - The busy bar shows "Formatting …" with a percentage.
   - The window can still be moved, resized and browsed.
   - Closing the window shows the "operation is still running" warning. Choose **Keep running**.
6. **Unplug during a full format.** Start another full format and unplug the stick halfway. You should see an error saying the disk was disconnected and the volume should be formatted again.

7. **Optional: a virtual disk.**
   1. In Disk Management, choose **Action → Create VHD**: 1 GB, saved anywhere, e.g. `C:\Temp\qa.vhdx`. Disk Management attaches it. Close any "Initialize disk" prompt it shows.
   2. In WinParted, the new **Msft Virtual Disk** appears. **Check the bus type says Virtual before doing anything.**
   3. Initialize it as GPT. You should see a 16 MB MSR and free space.
   4. Create a partition.
   5. Clear disk. The notification should offer **Initialize now**.
   6. Start a full exFAT format on it and, while it runs, plug in the stick. The stick appears and the format continues (*Other disk connected during format*).
   7. Afterwards, detach the VHD in Disk Management and delete the file.

Steps 5 and 6 cover *Long full format*, *Close attempt during shrink* (same warning for any operation) and *Disk removed mid-operation*. Step 7 covers *Offer after clear*, *Initialize new disk as GPT* in the app and *Other disk connected during format*.
