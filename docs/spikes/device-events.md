# Spike 2.1: device change notifications

Run on 2026-11-10, Windows 11 Pro, elevated (`spikes/WinParted.Spikes`, command `events`):
VHDX attach/initialize/partition/format/detach, then a Kingston DataTraveler USB stick plugged
in and removed.

## What arrived

| Change | SMP `MSFT_Storage*Event` | `Win32_VolumeChangeEvent` | `Win32_DiskDrive` instance events (`WITHIN 1`) |
|---|---|---|---|
| VHDX attached | Arrival, `SourceClassName=MSFT_PhysicalDisk`, at the moment attach completed | none (disk was RAW) | about 150 ms later |
| Initialize | **nothing** | nothing | nothing |
| CreatePartition | **nothing** | D: arrival (+0.8 s) and removal (+1.8 s): the temporary letter | nothing |
| Format | Arrival + Modification of an `MSFT_StorageJob` | nothing | nothing |
| VHDX detached | Departure, `MSFT_PhysicalDisk`, at the moment detach completed | nothing | about 470 ms later |
| USB stick plugged in | Arrival, `MSFT_PhysicalDisk` | D: arrival within 10 ms | about 770 ms later |
| USB stick removed | Departure, `MSFT_PhysicalDisk` | D: removal within 10 ms | about 380 ms later |

All disk arrivals and departures were reported well within the spec's 3-second budget. (Attaching
or detaching a VHDX with diskpart itself takes 11–17 s, once 3 minutes. That's diskpart, not the
events.)

## Decision for D9

- **SMP Arrival/Departure** are reliable and immediate for whole disks (they name `MSFT_PhysicalDisk`), so they are the primary source.
- **SMP sends no events for partition or volume changes** (Initialize, CreatePartition and DeleteObject produced none; Format produced only storage-job events). Changes made by other tools (Disk Management, diskpart) are therefore invisible to SMP events alone.
- Add **`Win32_VolumeChangeEvent`** (volume arrival/removal) as a second source. It catches volumes appearing or disappearing, including those made by other tools.
- Not detected by either source: changes that don't add or remove a volume, e.g. another tool resizing a partition. Manual refresh (F5) covers those. This is acceptable for the MVP.
- Every notification from any source goes through the 500 ms debounce, then a full snapshot reload (D3). Our own operations already refresh afterwards, so the events they cause are harmless duplicates that the debounce absorbs.
- `CM_Register_Notification` isn't needed.
