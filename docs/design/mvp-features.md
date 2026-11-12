# WinParted MVP: Design Brief

This document is the input for the WinParted UI design. It describes **what** the MVP must show and do: every screen, state, dialog, and the exact wording of reasons, warnings and errors. It does not prescribe visual style beyond the platform constraints below. Visual design, layout details, iconography and motion are up to the designer.

Source of truth for behavior: `openspec/changes/winparted-mvp/specs/`. If this brief and a spec disagree, the spec wins. Report the mismatch so the brief can be fixed.

---

## 1. Product summary

**WinParted** is a free, open-source (MIT) disk and partition manager for Windows 11, a modern, friendly take on GParted. It lets people see how their disks are divided and perform basic changes: create, delete, format and resize partitions, change drive letters and labels, and prepare or wipe whole disks.

### Who it's for

- **Primary:** a technically curious home user who must do one specific task: prepare a new SSD, wipe a USB stick, make room for a second partition, or understand why "Extend Volume" is greyed out. They aren't a storage expert and are nervous about losing data.
- **Secondary:** power users and IT people who know Disk Management and want a clearer, faster tool.

### Design principles

1. **Explain, don't just disable.** Every action the user can't take shows a short reason in plain language and, where possible, what to do instead. This is WinParted's main advantage over Windows Disk Management.
2. **Make data loss impossible to miss.** Destructive confirmations name the actual volume ("Photos (E:)", "120 GB used"), never "Partition 3". Clear disk requires typing the disk number.
3. **Show everything, protect what matters.** System partitions are always visible, but locked. Nothing is hidden.
4. **Immediate, honest operations.** There's no pending queue. Each action is validate, confirm, run, done. Long operations show progress and the app stays responsive.
5. **Calm by default.** Warnings only where they matter, so they still mean something.

---

## 2. Platform and constraints

| Topic | Constraint |
|---|---|
| UI framework | **WinUI 3** (Windows App SDK). Use **Fluent Design** and standard WinUI controls where possible (NavigationView or a custom list, InfoBar, ContentDialog, TeachingTip, ProgressBar/ProgressRing, NumberBox, ComboBox, Expander). Custom controls are fine where needed (the partition layout bar is custom). |
| OS | Windows 11 only. Use Windows 11 visuals freely: Mica backdrop, rounded corners, Windows 11 title bar and Fluent icons (Segoe Fluent Icons). |
| Themes | Light, Dark, and Windows High Contrast must all work. Follow the system theme. |
| Window | Resizable. Minimum **1000 x 640** effective pixels, which fits e.g. 1920 x 1080 at 150% scaling or 1366 x 768 at 100%. Smaller effective screens (such as 1280 x 720 at 150%) are not supported. |
| Rights | The app always runs as administrator (UAC prompt at launch). The title bar may show this subtly. |
| Language | English for MVP. Keep layouts tolerant of about 30% longer strings for future translation. |
| Units | Sizes use the same format as File Explorer: "512 GB" means 512 x 1024³ bytes, shown with three significant digits (e.g. "7.42 GB", "57.3 GB", "476 GB", "1.82 TB", "100 MB"). |
| Accessibility | Full keyboard operation (including selecting partitions in the layout bar), screen-reader names for every segment and action, never use color alone (combine with icon or text), and minimum contrast WCAG AA. |

---

## 3. Main window

### 3.1 Structure

The window has these regions (arrangement is up to the designer; the structure below is a suggestion):

```
+------------------------------------------------------------------+
| Title bar: WinParted                              [Refresh]      |
+-------------------+----------------------------------------------+
| DISK LIST         | SELECTED DISK                                |
|                   |  header: name, size, bus, style, status      |
|  Disk 0  (sys)    |  disk actions                                |
|  Disk 1           |                                              |
|  Disk 2  USB      |  PARTITION LAYOUT BAR                        |
|                   |  [EFI][MSR][====== C: ======][Rec][ free ]   |
|                   |                                              |
|                   |  DETAILS + ACTIONS for selected segment      |
|                   |  properties  |  actions (with reasons)       |
+-------------------+----------------------------------------------+
| (notifications: InfoBar / toast area)                            |
+------------------------------------------------------------------+
```

Selection model: one **disk** is selected in the list. In its layout bar, one **segment** (a partition or an unallocated region) may be selected. The details and actions area reflects the segment if one is selected, otherwise the disk.

### 3.2 Disk list

One entry per disk, showing:

- **Disk number** ("Disk 0") and **friendly name** ("Samsung SSD 980 PRO 1TB").
- **Total size**.
- **Bus type** as a short tag or icon: NVMe, SATA, USB, SD, Virtual (VHD/VHDX), Other.
- **Partition style**: GPT, MBR or "Not initialized".
- A small **overview of the layout** (a mini partition bar) is recommended so users can tell disks apart at a glance.
- **Status indicators** (icon + text, see section 4): System disk, Offline, Read-only, Health warning, Managed by Windows (dynamic / Storage Spaces), Busy (operation running).

Disks are ordered by disk number.

### 3.3 Selected disk header

- Number, friendly name, size, bus type, partition style.
- Operational status (Online / Offline) and health status (Healthy / Warning / Unhealthy, with Windows' text).
- Disk-level actions: **Initialize**, **Bring online / Take offline**, **Clear disk** (see section 6). Show relevant actions that are disabled, with the reason (section 7).

### 3.4 Partition layout bar (custom control)

The visual centerpiece. A horizontal bar for the selected disk:

- One segment per partition **and** per unallocated region larger than 1 MB, in on-disk order.
- Width proportional to size, with a **minimum width** so tiny partitions (e.g. 16 MB MSR on a 2 TB disk) are still visible and clickable. A small "not to scale" hint is fine.
- **Unallocated** segments must look clearly different from partitions (e.g. hatched or outlined).
- Each partition segment shows, space permitting:
  - Title: `Label (X:)`, or `X:` without label, or `Label` without letter, or a **type name**: "EFI System", "Microsoft Reserved", "Recovery", "Unformatted", "Unknown filesystem".
  - File system (NTFS, exFAT, FAT32, ReFS, or none).
  - Size.
  - **Used/free fill** for volumes Windows can read (e.g. a fill line or shaded portion at 60%).
  - **Lock badge** for protected segments (section 5).
- Selected segment has a clear selection state. Hover shows a tooltip with full info.
- Keyboard: Left/Right moves selection between segments; Enter/Applications key opens actions.
- If segment text doesn't fit, it collapses (title only, then icon only); full info is in the tooltip and details panel.

### 3.5 Details panel

Shows properties of the selected item.

**Partition:**
- Title (as on the segment) and plain-language role description (e.g. "Windows needs this to start").
- Partition number, partition type (GPT type name or MBR type), offset ("starts at 1.2 GB"), size.
- File system, label, drive letter and other mount paths.
- Used space, free space, percentage used.
- Flags shown only when true: System, Boot, Hidden, Active (MBR), Read-only, Encrypted (BitLocker locked/unlocked).

**Unallocated region:**
- Size, offset, and a hint: "You can create a new partition here."

**Disk** (no segment selected):
- Everything in the header plus: unique ID / serial (shortened, copyable), sector size, location (e.g. "USB port"), number of partitions, total unallocated space.
- Copy buttons confirm in place: the icon turns into a check mark with the tooltip "Copied" for two seconds (announced to screen readers). No notification.

### 3.6 Actions area

The actions available for the current selection, grouped logically. Disabled actions stay visible with their reason (section 7). How reasons are shown (inline text, info icon with TeachingTip, tooltip) is a design decision, but **the reason must be readable without hovering** for at least the most relevant disabled action (e.g. Extend on C:). Keyboard and screen-reader users must be able to reach it.

| Selection | Actions |
|---|---|
| Disk | Initialize, Bring online / Take offline, Clear disk |
| Partition | Format, Delete, Shrink, Extend, Change drive letter, Change label |
| Unallocated region | Create partition |

### 3.7 Refresh

A **Refresh** button (and F5) reloads all information. Selection is kept if the item still exists; if a selected partition is gone, the selection moves to its disk.

---

## 4. States the design must cover

### 4.1 App-level states

| State | Content |
|---|---|
| **Loading** (startup, refresh) | Skeleton or progress indicator in place of the disk list and layout; no empty flash. Refresh after startup should not blank the UI (show a subtle indicator instead). |
| **Load error** | "WinParted couldn't read disk information." + short explanation + **Try again** button. Technical details expandable. |
| **Missing administrator rights** | Full-window message: "WinParted needs administrator rights to manage disks." + **Restart as administrator** button. Shown only if the app somehow starts unelevated. |
| **Operation running** | See section 8. Browsing still works; all modifying actions disabled with reason "Another operation is in progress." |

### 4.2 Disk states

| State | How it looks / behaves |
|---|---|
| **Normal** | Layout bar with segments. |
| **System disk** | Badge "System disk". Initialize, Clear, Take offline disabled. Unprotected partitions and free space on it are still editable. |
| **Not initialized** | No layout; instead an empty-state panel: "This disk isn't set up yet." + **Initialize** as primary action. Clear disk stays visible but disabled ("This disk is already empty."). |
| **Offline** | Layout replaced with "This disk is offline. Its volumes aren't available in Windows." + Windows' offline reason if any (e.g. "Policy", "Signature collision") + **Bring online** as primary action. |
| **Read-only** | Badge "Read-only". Every modifying action disabled. Typical case: SD card with write-protect switch. Exception: if the disk is also **offline**, Bring online stays available (it writes no data); once online, the disk shows the normal read-only state. |
| **Health warning / unhealthy** | Warning badge + Windows' health text. Actions still allowed (subject to other rules), but the user should notice. |
| **Managed by Windows** (dynamic disk, Storage Spaces) | Lock badge + "Managed by Storage Spaces" / "Dynamic disk - managed by Disk Management". Everything read-only. |
| **Busy** | Operation running on this disk; progress shown (section 8). |

### 4.3 Segment states

| State | Notes |
|---|---|
| Normal volume | Label/letter, FS, size, fill. |
| Protected (locked) | Lock badge + role description (section 5). |
| Unformatted (RAW) | "Unformatted"; Format and Delete available. |
| Unknown filesystem (e.g. Linux ext4) | "Unknown filesystem", no used/free; only Delete available. |
| BitLocker locked | Lock badge + "Locked by BitLocker". |
| BitLocker unlocked | Small "Encrypted" indicator; normal actions. |
| No drive letter | Title from label or type; details show "No drive letter". |
| Unallocated | Distinct look; "Unallocated" + size. |
| Selected / hover / focus | Clear, accessible states. |

### 4.4 Empty state

If no disks are found (unusual): "No disks found." + **Refresh**.

---

## 5. Protected items (lock model)

Protected items are **always visible**, carry a **lock badge** and a **one-line role description**, and the blocked actions show the reason. There is **no override** in the MVP: no "advanced mode" or "unlock" option anywhere.

| Item | Role description | Allowed actions |
|---|---|---|
| EFI System partition | "EFI System - Windows needs this to start" | none |
| Microsoft Reserved (MSR) | "Microsoft Reserved - used by Windows, contains no files" | none |
| Recovery partition | "Recovery - Windows uses this to repair itself" | none |
| Other boot/system partition (e.g. MBR "System Reserved") | "System partition - Windows needs this to start" | none |
| Windows volume (usually C:) | "Windows is installed here" | Shrink, Extend, Change label |
| Pagefile / crash dump volume | "Windows uses this volume for virtual memory" | Shrink, Extend, Change label |
| Locked BitLocker volume | "Locked by BitLocker" | none |
| Dynamic disk (whole) | "Dynamic disk - managed by Disk Management" | none |
| Storage Spaces member (whole) | "Managed by Storage Spaces" | none |
| System disk (disk level) | "System disk - Windows is installed on this disk" | disk-level: none; partition-level: per item |
| Read-only disk (whole) | "This disk is write-protected" | none |

---

## 6. Dialogs

All modifying operations use a dialog (ContentDialog or similar). Common rules:

- **Title** = action + target, e.g. "Format Data (D:)?", "Clear Disk 3?".
- Body states **what will change** and **what will be lost**, naming target by label, letter and disk number.
- **Primary button** names the action ("Format", "Delete", "Clear disk"), never "OK". Destructive primary buttons use a destructive style. **Cancel** is always available.
- **Keyboard, the same in every dialog:** Enter presses the highlighted (default) button, and Escape cancels. The primary button is the default while the input is valid, so Enter in a field confirms with the value as typed (the preview already shows it). While the primary button is disabled (invalid input, wrong disk number, minimum size still being calculated), no button is highlighted and Enter does nothing. Delete partition has nothing to type, so **Cancel is its default** and Enter cancels.
- Invalid input disables the primary button and shows the reason next to the field.
- If the target disk is disconnected or changes while the dialog is open, the dialog **closes itself** and a notification says: "This disk was disconnected or changed. Nothing was modified."
- Cancel never changes anything.

### 6.1 Initialize disk

- Target: uninitialized disk.
- Field: **Partition style**, a choice between:
  - **GPT (recommended)**: "Works with all modern PCs and disks larger than 2 TB." *(preselected)*
  - **MBR**: "Only needed for very old PCs or devices that don't support GPT. Limited to 2 TB and four partitions."
- Warning when MBR is chosen for a disk larger than 2 TB: "With MBR, only the first 2 TB of this 4 TB disk can be used."
- Primary: **Initialize**.
- Result: an MBR disk shows one unallocated region. A GPT disk shows a 16 MB **Microsoft Reserved** partition (Windows creates it automatically; locked) followed by one unallocated region.

### 6.2 Take offline / Bring online

- **Take offline** confirmation: "Take Disk 2 offline?" / "Its volumes (F:) won't be available in Windows until you bring the disk online again. No data is deleted." Primary: **Take offline** (non-destructive style).
- **Bring online** can run without a confirmation (non-destructive). A progress/success notification is enough.

### 6.3 Clear disk (strongest confirmation)

- Title: "Clear Disk 3?"
- Shows: **friendly name, size, bus type** (e.g. "SanDisk Ultra USB 3.0 - 57.3 GB - USB"), and a **labelled partition bar** of the disk with **used space per volume** (e.g. "BACKUP (E:) 41 GB used").
- Text: "All partitions and files on this disk will be erased. This cannot be undone."
- Input: "Type **3** to confirm:" with a short text box. The primary button stays disabled until the exact disk number is typed.
- Primary: **Clear disk** (destructive).
- After success: notification "Disk 3 was cleared." with action **Initialize now**, which opens the Initialize dialog for that disk. On USB sticks Windows keeps an empty MBR partition table, so the disk shows as empty MBR with only unallocated space; then the action is **Create partition** instead.

```
+-----------------------------------------------------------+
|  Clear Disk 3?                                            |
|                                                           |
|  SanDisk Ultra USB 3.0  -  57.3 GB  -  USB                |
|  [== BACKUP (E:) 41 GB used ==][ free ]                   |
|                                                           |
|  All partitions and files on this disk will be erased.    |
|  This cannot be undone.                                   |
|                                                           |
|  Type 3 to confirm:  [   ]                                |
|                                [Cancel]  [Clear disk]     |
+-----------------------------------------------------------+
```
*(Wireframe for content only; visual design is open.)*

### 6.4 Create partition

- Target: an unallocated region.
- Fields:
  - **Size**: number input + unit (MB / GB), default = the whole region. Range 1 MB to region size. Optionally a slider or visual preview showing the new partition at the start of the region and the remainder staying free.
  - **Format**: on by default; option **Don't format** (creates an empty partition with no file system and no drive letter).
  - **File system**: NTFS (default), exFAT, FAT32. Unavailable options are disabled with the reason (section 7.3). ReFS is not offered for formatting in the MVP (existing ReFS volumes are still shown and can be resized).
  - **Volume label**: empty by default; validated per file system (section 7.3).
  - **Drive letter**: first free letter by default; dropdown of free letters plus "No drive letter".
  - **Quick format**: on by default.
- When "Don't format" is selected, the file system, label, letter and quick format fields are disabled.
- MBR limit: if no more partitions fit, Create partition is disabled before the dialog (section 7).
- Primary: **Create**.

### 6.5 Delete partition

- Title: "Delete Photos (E:)?"
- Body: "All files on Photos (E:) on Disk 2 will be permanently erased (120 GB used). The space will become unallocated."
- Second line (volumes only): "Programs using Photos (E:) will lose access to it and may lose unsaved work. Close them first." Windows disconnects them instead of refusing, so this warning replaces any "volume in use" error.
- Unknown filesystem variant: "Windows can't read this partition (it may be used by Linux or another operating system). Its contents will be permanently erased."
- Unformatted variant: no used-space line.
- Primary: **Delete** (destructive).

### 6.6 Format volume

- Title: "Format Data (D:)?"
- Fields: **File system** (default: current, or NTFS), **Volume label** (default: current), **Allocation unit size** (default "Default (recommended)", then values Windows supports), **Quick format** (on).
- **Quick format** is available for every file system. One exception: on a virtual disk (VHD/VHDX) with NTFS selected, full format is disabled with the reason "Windows can't do a full NTFS format on a virtual disk. Quick format works."
- A full format reports real percentage progress (about 45 s per GB on a typical USB stick), so the progress bar is determinate; a quick format takes a few seconds.
- When quick format is off, explanation: "A full format checks the whole partition for errors. It can take a long time and can't be stopped once started."
- Body warning: "All files on Data (D:) will be erased (37 GB used)."
- Second line: "Programs using Data (D:) will lose access to it and may lose unsaved work. Close them first."
- File system choices: NTFS, exFAT, FAT32 (no ReFS in the MVP).
- Drive letter is kept.
- Primary: **Format** (destructive).

### 6.7 Shrink volume

- Only NTFS / ReFS.
- **Loading state:** Windows can take up to about half a minute to report the smallest possible size of a large volume (22 s measured on a 476 GB system volume). While it calculates, show "Calculating the smallest possible size…" with an indeterminate indicator; the size input and Shrink stay disabled, Cancel works.
- Then shows **current size**, **minimum size** and **new size** input (number + unit, plus optional slider). A preview of the result: the volume gets smaller and **unallocated space appears directly after it**.
- If minimum is notably larger than used space, explanation: "Windows can't move some system files (like the pagefile or hibernation file), so this volume can't be made smaller than 180 GB right now."
- Note: "Shrinking can take several minutes and can't be stopped once started."
- Primary: **Shrink** (non-destructive style; data is kept).

### 6.8 Extend volume

- Only NTFS / ReFS, only into **unallocated space directly after** the volume.
- Shows current size, available space after it, and **new size** (default: maximum).
- Primary: **Extend**.

### 6.9 Change drive letter

- Dropdown of free letters + "No drive letter".
- When removing a letter: "Programs and shortcuts that use E: may stop working. The files on the volume are not affected."
- When changing: "Programs and shortcuts that use E: may need to be updated."
- Primary: **Change** / **Remove letter**.

### 6.10 Change label

- Text field prefilled with the current label.
- Live validation: NTFS/ReFS max 32 characters; exFAT max 15 characters; FAT32 max 11 characters and no `* ? . , ; : / \ | + = < > [ ] "`. Messages: "FAT32 labels can be at most 11 characters." and "FAT32 labels can't contain * ? . , ; : / \ | + = < > [ ] or quotes."
- Primary: **Save**.

### 6.11 Close while an operation is running

- Title: "An operation is still running"
- Body: "Formatting Data (D:) is still in progress. Closing WinParted will not stop it. Windows will finish it in the background, but you won't see the result here."
- Buttons: **Keep WinParted open** (default), **Close anyway**.

---

## 7. Disabled-action reasons (copy)

Wording can be polished, but each reason must name the **specific obstacle** and, where possible, **what to do**. `{…}` are placeholders.

### 7.1 Global

| Situation | Reason text |
|---|---|
| Another operation running | "Another operation is in progress. Wait for it to finish." |
| Disk is read-only | "This disk is write-protected. If it has a lock switch, turn it off." |
| Dynamic disk | "This is a dynamic disk. WinParted can't change dynamic disks. Use Disk Management." |
| Storage Spaces | "Managed by Storage Spaces. Use Storage Spaces in Settings to change it." |
| Disk offline | "This disk is offline. Bring it online first." |

### 7.2 Protected items

| Situation | Reason text |
|---|---|
| EFI / system partition | "Windows needs this partition to start. Changing it could make your PC unbootable." |
| MSR | "Windows reserves this partition. It contains no files and must not be changed." |
| Recovery | "Windows uses this partition to repair itself." |
| Windows volume, delete/format/letter | "Windows is running from this volume." |
| Pagefile volume, delete/format/letter | "Windows uses this volume for virtual memory." |
| Locked BitLocker | "This volume is locked by BitLocker. Unlock it in File Explorer first." |
| System disk: Initialize/Clear/Offline | "Windows is installed on this disk." |
| Unknown protection state (could not be checked) | "WinParted couldn't confirm this item is safe to change, so it's protected." |

### 7.3 Operation-specific

| Situation | Reason text |
|---|---|
| Extend blocked by a following partition | "{Volume} can only grow into free space directly after it. The {Next partition} partition is in the way." *(e.g. "C: can only grow into free space directly after it. The Recovery partition is in the way.")* |
| Extend: free space only before the volume | "A volume can only grow into free space after it. Moving partitions isn't supported." |
| Extend: no free space on disk | "There's no unallocated space after {Volume}." |
| Shrink/Extend on non-NTFS/ReFS | "Only NTFS and ReFS volumes can be resized. This volume is {FS}." |
| Shrink: already at minimum | "This volume can't be made any smaller right now." |
| Resize on unformatted | "Format this partition before resizing it." |
| Initialize on initialized disk | "This disk is already set up. To start over, use Clear disk." |
| Clear on a disk that isn't initialized | "This disk is already empty." |
| Take offline on removable media (USB flash drive, SD card) | "Windows can't take removable media, like USB flash drives, offline." |
| Create: MBR limit | "MBR disks are limited to four primary partitions." |
| Any action except Delete on unknown filesystem (Format, Shrink, Extend, letter, label) | "Windows can't read this partition. You can only delete it." |
| Naming the volume in a reason | Use the drive letter alone when the volume has one ("D: can only grow…"), otherwise its title ("Archive can only grow…"). |
| Label/letter on unformatted | "Format this partition first." |
| FAT32 option on > 32 GB | "WinParted formats FAT32 up to 32 GB, like Windows does. Use exFAT for larger drives." |
| No free drive letters | "All drive letters are in use." |

---

## 8. Running operations

- **Immediate:** after confirmation the operation starts at once. There is no queue.
- **One at a time:** while running, all modifying actions everywhere are disabled ("Another operation is in progress."). Browsing, selecting and viewing details keep working.
- **Progress:** show "{Action} {target}", e.g. "Formatting Data (D:)", "Shrinking C:", with a percentage when Windows reports one and an indeterminate indicator otherwise. The affected disk and segment show a busy state.
- **Success:** layout refreshes; short notification, e.g. "Data (D:) was formatted as NTFS."
- **Failure:** a notification or dialog with:
  1. what happened, in plain language
  2. **whether anything was changed**
  3. what the user can do
  4. an expandable **Technical details** section (error code, raw message, copy button)

### 8.1 Error message examples

| Case | Message |
|---|---|
| Disk is read-only (became so during the dialog) | "Disk 2 is write-protected. Nothing was changed." |
| Access denied | "Windows denied access to this disk. Nothing was changed." |
| Not enough space / size not supported | "Windows couldn't resize the volume to that size. Nothing was changed. Try a smaller change." |
| Target changed before running | "This disk was disconnected or changed. Nothing was modified." |
| Disk removed during operation | "Disk 3 was disconnected while formatting. The volume may be incomplete. Format it again before using it." |
| Partition created, format failed | "The partition was created, but formatting failed. It's shown as Unformatted. You can try formatting it again." |
| Unknown error | "Something went wrong and Windows didn't say why. Check Technical details. The disk may have been partly changed. Refresh to see its current state." |

---

## 9. Device changes (hot-plug)

- Disks appearing or disappearing update the list automatically within about 3 seconds.
- Non-blocking notification: "USB disk connected: SanDisk Ultra (57.3 GB)" / "Disk 3 was disconnected".
- If the **selected** disk disappears: the first remaining disk is selected (the main area is never left empty) and the notification explains: "Disk 3 was disconnected. Showing Disk 0 instead."
- If a **dialog's** disk disappears or changes: the dialog closes (section 6, common rules).
- Changes to **other** disks never disturb an open dialog or a running operation.

---

## 10. Sample data for mockups

**Disk 0**: Samsung SSD 980 PRO 1TB, 931 GB, NVMe, GPT, Healthy, **System disk**

| # | Segment | FS | Size | Used | Notes |
|---|---|---|---|---|---|
| 1 | EFI System | FAT32 | 100 MB | n/a | locked |
| 2 | Microsoft Reserved | none | 16 MB | n/a | locked |
| 3 | Windows (C:) | NTFS | 830 GB | 412 GB | Windows volume, pagefile |
| 4 | Recovery | NTFS | 750 MB | n/a | locked |
| n/a | Unallocated | n/a | 100 GB | n/a | |

*(This layout shows the "Extend C: blocked by Recovery" reason.)*

**Disk 1**: WDC WD20EZAZ, 1.82 TB, SATA, GPT, Healthy

| # | Segment | FS | Size | Used |
|---|---|---|---|---|
| 1 | Data (D:) | NTFS | 1.2 TB | 740 GB |
| 2 | Unknown filesystem | n/a | 300 GB | n/a |
| n/a | Unallocated | n/a | 320 GB | n/a |

**Disk 2**: SanDisk Ultra USB 3.0, 57.3 GB, USB, MBR, Healthy

| # | Segment | FS | Size | Used |
|---|---|---|---|---|
| 1 | BACKUP (E:) | exFAT | 57.3 GB | 41 GB |

**Disk 3**: Generic SD/MMC, 29.7 GB, SD, **Not initialized**

**Disk 4**: Msft Virtual Disk, 64 GB, Virtual, GPT, **Offline**

---

## 11. Out of scope for MVP (don't design)

- Check & repair (chkdsk), defragment/optimize.
- Moving or copying partitions; resizing from the left.
- A pending-operations queue / "Apply" step.
- Converting between MBR and GPT without data loss.
- Linux filesystems (beyond showing "Unknown filesystem" and allowing delete).
- Storage Spaces, dynamic disks, RAID, BitLocker management (beyond showing state).
- Settings, update checks, telemetry, onboarding tours.

An **About** entry (version, MIT license, GitHub link) is in scope but can be minimal.

---

## 12. Deliverables requested from design

1. Main window in light and dark theme, using the sample data, with Disk 0 and Disk 1 selected variants.
2. The partition layout bar component with all segment states (section 4.3), including narrow and collapsed variants.
3. Disk states: not initialized, offline, read-only, managed by Windows, unhealthy.
4. App states: loading, load error, missing admin rights, empty.
5. Every dialog in section 6, including validation-error and disabled-option states.
6. How disabled-action reasons are presented (inline and on demand).
7. Running-operation state (determinate and indeterminate), success and error notifications (with Technical details expanded).
8. Hot-plug notifications.
9. A component and token summary that maps to WinUI 3 / Fluent controls and theme resources.
