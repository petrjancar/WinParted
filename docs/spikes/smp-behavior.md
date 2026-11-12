# Spike 2.3: SMP behavior and status codes

Run on 2026-11-10, Windows 11 Pro (build 26300), elevated, on throwaway VHDX files
(`spikes/WinParted.Spikes`, commands `facts` and `behavior`). Raw logs stay local in `spikes/output/`.

## Reading disks

| Finding | Consequence |
|---|---|
| `MSFT_Partition.DiskId` equals `MSFT_Disk.Path`, **not** `MSFT_Disk.ObjectId`. | Storage must join partitions to disks on `Path`. |
| Listing `MSFT_Disk`, `MSFT_Partition`, `MSFT_Volume` works without admin rights. `GetSupportedSize` and `Win32_EncryptableVolume` need admin. | None (the app always runs elevated). |
| `GetSupportedSize` on a 476 GB system volume took **21.9 s**. On a 4 GB test volume it took 75 ms. | Never call it during a snapshot read. Call it only when the Shrink dialog opens, with a loading state. `Partition.MinimumSize` stays null in snapshots. |
| An unformatted partition's volume reports `FileSystem = ""` (empty string). | Map `""` and `"RAW"` to `FileSystemKind.None`. |
| `GetSupportedFileSystems` returned only `["NTFS"]`, both for the NTFS system volume and for a new 40 GB RAW volume, which was then formatted as FAT32 and exFAT without error. | **Format options can't come from SMP.** Design D6/D8 must use a fixed list (NTFS, exFAT, FAT32, ReFS) instead. |
| `GetSupportedClusterSizes("NTFS")` returns `4096 … 2097152`. | Usable for the allocation-unit list. |
| BitLocker: `Win32_EncryptableVolume.GetLockStatus` works elevated (C: has `ProtectionStatus=1`, `LockStatus=0`, i.e. encrypted and unlocked). | Approach in D6 confirmed. |
| Storage Spaces: only the primordial pool exists. `MSFT_PhysicalDisk.CannotPoolReason` returns reason codes (7 = insufficient capacity on this PC). | Membership = `CannotPoolReason` contains 2 ("In a pool"). |
| SAN policy is 1 (Online All). Attached VHDX files arrive online and writable. | Offline + read-only disks are rare on this PC. Option B stays a safety net. |

## Layout facts

- **GPT `Initialize` creates a 16 MB Microsoft Reserved partition** at offset 17,408 (hidden). A new GPT disk is therefore "MSR + unallocated", not "one unallocated region". The first data partition then starts at 16 MB.
- MBR partitions created by SMP start at **64 KB** on a 1 GB disk (not 1 MB) and get MBR type **0x06** before formatting.
- **MBR: SMP creates an extended partition automatically.** Three `CreatePartition` calls made three primaries. The fourth created an extended container (`MbrType=5`, `PartitionNumber=0`) spanning **all remaining space**, plus a logical partition inside it. The fifth and sixth were more logicals in the container, about 64 KB apart. SMP never refused. Core's rule (free space inside a container is always usable, and the 4-entry limit applies only outside it) matches this behavior. The limit only shows up on disks partitioned by other tools.
- Logical partitions inside the container are listed as normal partitions, and the gaps between them are under 1 MB. `LayoutBuilder` (container skipped, gaps of 1 MB or less ignored) shows them correctly.

## SMP does not protect anything

- `DeleteObject` on the **Microsoft Reserved** partition succeeded.
- `CreatePartition` on an **offline** disk succeeded.
- `DeleteObject` called a second time on an **already deleted** partition returned 0 (success).
- **`Format` succeeded while a file on the volume was held open** (no `Force`), and so did `DeleteObject`. SMP dismounts the volume, so open handles in other programs are cut off.

So every safety rule has to live in WinParted, and the "volume in use" failure the design anticipated does **not** happen. The user should be warned before the operation instead.

## Drive letters

- `CreatePartition(AssignDriveLetter=false)` still **briefly mounted the new partition as D:** (a volume arrival, then a removal about 1 s later).
- A partition created while the disk was offline **received D: automatically** when the disk came back online.

Consequence: after Create with "No drive letter", re-read the partition and remove any letter Windows assigned. Also be aware that Explorer may briefly show its "format this disk" prompt for a RAW partition.

## Return codes observed

| Code | Situation | Meaning / message |
|---|---|---|
| 0 | Success | |
| 1 | `Format(Full=true)` on an expandable VHDX | Not supported. Still open: does this happen on fixed VHDX or real disks? (See smp-progress.md.) |
| 5 | `SetFileSystemLabel` with 16 characters on exFAT | Invalid parameter |
| 4096 | `Format(RunAsJob=true)` | Job started |
| 4097 | `Resize` below the minimum; `Resize` to the current size | Size not supported. `ExtendedStatus.Message` carries the text, e.g. "The specified shrink size is too big and will cause the volume to be smaller than the minimum volume size." or "The partition is already the requested size." (MessageID 8030) |
| 40000 | `CreatePartition` larger than the free space; `Resize` above the maximum | Not enough available capacity |
| 41001 | `Initialize` on an initialized disk | The disk has already been initialized |
| 41002 | `CreatePartition` on a read-only disk | The disk is read-only |
| CimException `NotFound` (0x80041002) | Unknown method parameter (`Resize(RunAsJob)`) | Programming error, not a user error |

`ExtendedStatus` is an embedded `MSFT_StorageExtendedStatus` with an English `Message` and a `MessageID`. It should go into "Technical details".

## Option B check

`Online()` on an offline **and** read-only disk succeeded, and the disk stayed read-only afterwards, which confirms the spec's "Offline and read-only disk" scenario.

## Clear disk on a removable USB stick (2026-11-11)

On the Kingston USB stick, `Clear(RemoveData, RemoveOEM)` returned 0, but the disk **stayed
initialized as MBR** (empty) instead of becoming "not initialized". The following
`Initialize(GPT)` then returned **41001** (already initialized). This happened twice. Windows keeps
an MBR partition table on removable media.

Consequences:
- After Clear disk, show whatever state Windows reports: "Not initialized" (fixed disks) or an
  empty MBR disk with only unallocated space (USB sticks).
- Offer "Initialize now" only if the disk really ended up not initialized. Otherwise offer
  "Create partition" for the free space.
- A USB stick can't be switched to GPT through Clear + Initialize. Not needed for the MVP.
