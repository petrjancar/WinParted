# Spike 2.2: progress reporting for Format and Resize

Run on 2026-11-10, Windows 11 Pro, elevated, on a 4 GB expandable VHDX (`spikes/WinParted.Spikes`,
command `progress`).

## Results

| Operation | Mechanism | Result |
|---|---|---|
| Quick format NTFS, 4 GB | sync call | 2.6 s, return 0 |
| Full format NTFS | async `InvokeMethodAsync` with `EnableMethodResultStreaming` + `WriteProgress` | `WriteProgress` callbacks arrived ("Formatting volume", 0%, then 100%; `secondsRemaining` = 0xFFFFFFFF = unknown). **Return value 1 (not supported)** after 2.6 s. |
| Full format NTFS | `RunAsJob=true` | Returned 4096 (job started); the job disappeared from `MSFT_StorageJob` within 40 ms. No usable progress. |
| Shrink 4.0 → 2.0 GB with 1.5 GB of data | async + callbacks | **No callbacks at all.** Finished in 567 ms. |
| Extend | `RunAsJob=true` | Not a parameter of `Resize` (CimException NotFound). |

## Conclusions

- **Format reports progress through `WriteProgress` callbacks** on an async call. That's the mechanism the runner uses (`IProgress<OperationProgress>`). Only 0% and 100% were seen on a fast volume. Large real volumes may report intermediate values, but this wasn't verified.
- **Resize reports no progress.** The UI uses the indeterminate indicator, as the design already shows for "Shrinking C:".
- `RunAsJob` isn't worth using: it's unsupported on Resize and gives nothing useful on Format.
- Full (non-quick) format returned 1 (not supported) on an expandable VHDX. The follow-up below repeats it on a fixed-size VHDX.
- `secondsRemaining` is always unknown, so there's no time estimate in the UI.

## Follow-up (task 2.4): full format on fixed vs expandable VHDX

Run on 2026-11-11 (`fullformat` command), on a 1 GB **fixed** VHDX (fully allocated, like a real
disk) and a 1 GB expandable one.

| Call | Fixed VHDX | Expandable VHDX |
|---|---|---|
| Quick format NTFS | 0 (2.6 s) | 0 (2.6 s) |
| Full format NTFS, sync | **1** (65 ms) | **1** (63 ms) |
| Full format NTFS, async with progress | `WriteProgress` 0% → 100%, then **1** (2.6 s) | 100% at once, then **1** (68 ms) |
| Full format exFAT, async with progress | 0% → 100%, return **0** (2.6 s) | 0% → 100%, return **0** (2.6 s) |
| Full format NTFS with `Force=true` | **1** | **1** |

- **SMP refuses a full NTFS format with 1 ("not supported") on both kinds of virtual disk**, while a
  full exFAT format succeeds. Thin provisioning is not the cause, since fixed and expandable behave
  the same. On a real disk full NTFS works (see the USB follow-up below).
- The progress callbacks arrive even for the call that ends up failing, so the UI must not treat
  "100%" as success. Only the return value counts.
- **Drive letters:** 3 s after `CreatePartition(AssignDriveLetter=false)` neither partition had a
  drive letter. The letter seen in spike 2.1 exists only while the partition is being created. The
  "remove a stray letter" step (task 9.2) stays as a cheap safeguard; it found nothing to remove here.

## Follow-up: real USB flash disk (Kingston DataTraveler 3.0, 14.4 GB)

Run on 2026-11-11 (`usb` command, with guarded selection and typed confirmation), on a 1 GB partition.

| Format | Quick | Full | Progress during full format |
|---|---|---|---|
| NTFS | 0 (2.9 s) | **0 (45.8 s)** | 0, 9, 14, 20 … 99, 100 %, a callback about every 2.5 s |
| exFAT | 0 (2.7 s) | **0 (45.8 s)** | same pattern |
| FAT32 | 0 (2.7 s) | **0 (40.7 s)** | same pattern |
| NTFS full, sync, `AllocationUnitSize=4096` | | 0 (43.2 s) | |
| NTFS full, sync, with label | | 0 (48.2 s) | |
| `format.com` full NTFS (no /Q) | | exit 0 (44.0 s) | |

**Conclusions**

- **SMP does a full format for every file system on a real disk.** The refusal (return 1) only
  happens for full NTFS on *virtual* disks (VHDX, fixed or expandable). Quick and full format can
  be offered for every file system without a fallback tool. `format.com` also works but isn't needed.
- **Full format reports real, smooth progress**: about 22 MB/s on this stick, so roughly 11 minutes
  for 14 GB. Quick format reports nothing in between (about 3 s).
- On virtual disks, full NTFS stays refused. WinParted shows that choice disabled with a reason
  rather than letting it fail. Integration tests on VHDX can only cover full format for exFAT and
  FAT32; full NTFS is covered by the manual QA checklist on a USB stick.
