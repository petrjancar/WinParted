# WinParted spikes

Throwaway experiments that answer questions about the Windows Storage Management API before the
real disk code is written (tasks group 2 in `openspec/changes/archive/2026-11-12-winparted-mvp/tasks.md`). Not part of
`WinParted.sln`.

## Safety

- `facts` only reads. It changes nothing.
- `behavior`, `progress` and `events` create temporary virtual disks (VHDX files in
  `%TEMP%\WinPartedSpike`), attach them, partition and format **only those**, then detach and delete
  them. Before every change the tool re-reads the disk and refuses anything that isn't a
  file-backed virtual disk of the expected size, or that holds Windows.
- The 64 GB test disk is sparse: it uses almost no real disk space.
- `events` optionally asks you to plug in and unplug a USB stick. Nothing is written to it.

## Running

Open **Terminal (Admin)**, then:

```bash
cd path\to\WinParted
dotnet run --project spikes/WinParted.Spikes -- all
```

Or one at a time: `facts`, `behavior`, `progress`, `events`; `vhd` runs the last three. If a run is interrupted, `cleanup`
detaches and deletes leftover VHDX files.

Logs go to `spikes/output/` (ignored by git, since they contain machine-specific disk details).
