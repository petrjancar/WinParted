# WinParted: Installer and Updates Design Brief

This document is the input for the UI design of the `add-installer-and-updates` change. It covers only what's new: the extended **About** dialog and the **update notice**. Everything else (visual system, platform constraints, main window, notifications area) is unchanged from the MVP brief, [mvp-features.md](mvp-features.md), and the existing app. Visual design and layout details are up to the designer.

Source of truth for behavior: `openspec/specs/` (`app-info`, `update-notice`, `installation`), added by the `add-installer-and-updates` change (now in `openspec/changes/archive/2026-11-14-add-installer-and-updates/`). If this brief and a spec disagree, the spec wins. Report the mismatch so the brief can be fixed.

---

## 1. What's new

- WinParted can now be **installed** (with a Start menu entry) and **uninstalled** completely. The installer and uninstaller use the standard Windows setup wizard and need no design (section 5).
- When WinParted starts, it asks GitHub whether a newer version exists. If so, a **non-blocking notice** in the main window says so and links to the release page. The user downloads and installs it themselves; nothing happens automatically.
- The **About** dialog gets a link to the GitHub repository and two update settings.

Design principles from the MVP brief apply, especially **"Calm by default"**: the update notice must never compete with disk work, warnings or errors.

---

## 2. Constraints

Same as MVP brief section 2 (WinUI 3, Fluent, Windows 11, light/dark/High Contrast, English, keyboard and screen-reader access, WCAG AA). In addition:

- Use standard controls: `ContentDialog` for About, `HyperlinkButton` for the repository link, `ToggleSwitch` for the settings, `InfoBar` for the update notice (the same control as the app's other notifications).
- About must work on **every screen**, including "needs administrator rights", where the settings can't be changed.

---

## 3. About dialog

Opened from the **About** (info icon) button in the title bar. Today it shows: title "WinParted", the description, "Version 0.1.0" and "MIT License", and a **Close** button.

### 3.1 Contents

In this order (grouping and spacing up to the designer):

| Element | Content |
|---|---|
| Title | **WinParted** |
| Description | "A free, open-source disk and partition manager for Windows 11." |
| Version | "Version 0.2.0" |
| License | "MIT License" |
| Repository link | **github.com/petrjancar/WinParted** (opens https://github.com/petrjancar/WinParted in the default browser; the dialog stays open) |
| Section heading | "Updates" |
| Switch 1 | **Check for updates when WinParted starts** (default: On) |
| Switch 2 | **Include pre-releases** (default: Off). Secondary text: "Early versions that may be less tested." |
| Privacy note | "WinParted asks GitHub for the newest version when it starts. Nothing about your PC or disks is sent." |
| Button | **Close** (highlighted; Enter presses it, Escape closes) |

Changing a switch saves immediately and takes effect at the next start. There is no Save button.

### 3.2 States

| State | What changes |
|---|---|
| **Default** (running as administrator, checks on) | Both switches enabled; "Include pre-releases" Off. |
| **Checks off** | "Check for updates…" Off; "Include pre-releases" shown **disabled**, keeping its value. |
| **Without administrator rights** (the "needs administrator rights" screen) | Both switches shown **disabled**, with the line: "Restart WinParted as administrator to change these settings." Everything else works, including the link. |

### 3.3 Keyboard and screen reader

- Tab order: repository link, "Check for updates…" switch, "Include pre-releases" switch, Close.
- Each switch is announced with its full label and state; the link is announced as "github.com/petrjancar/WinParted, link".
- Disabled switches stay readable (contrast) and are announced as unavailable.

---

## 4. Update notice

### 4.1 Content

Shown in the existing notifications area as an **informational** InfoBar (not warning or error).

| Case | Text | Action |
|---|---|---|
| Newer full release | "WinParted 0.3.0 is available." | **View release** |
| Newer pre-release (only with "Include pre-releases" on) | "WinParted 0.4.0 is available. This is a pre-release." | **View release** |

- **View release** opens the release page on GitHub in the default browser. The notice stays until closed.
- The close (X) button dismisses it. It doesn't come back until WinParted is started again.
- There's no "Update now", "Remind me later" or "Skip this version": the notice is shown at most once per start and costs one click to dismiss.

### 4.2 Behavior

- **Timing:** appears a few seconds after start, once the disk list has loaded. It never delays startup.
- **Never during an operation:** if an operation is running, the notice waits until the operation has finished and its success or error notification has appeared, then appears below or after it.
- **No focus:** it doesn't take keyboard focus or interrupt typing in a dialog. Screen readers announce it once ("WinParted 0.3.0 is available").
- **Stays out of the way:** it must not cover the partition layout bar or the actions area at the minimum window size (1000 x 640).
- **Coexists** with other notifications (operation results, hot-plug). Error notifications must remain the most visible.
- **Silent when nothing to say:** no notice when WinParted is up to date, offline, or the check fails, and no "You're up to date" message.

---

## 5. Installer and uninstaller (no design needed)

The installer and uninstaller are standard Inno Setup wizards with Windows' own look: welcome, install location (default `C:\Program Files\WinParted`), progress, finish (with "Launch WinParted"). Their texts are Inno Setup's standard English texts. They aren't part of this design request.

The only visual asset involved is the **app icon**, already used by the app (title bar, taskbar). It appears in the Start menu, Installed apps and the installer's window.

---

## 6. Sample data for mockups

- Running version: **0.2.0**
- Newest full release: **0.3.0**
- Newest pre-release: **0.4.0** (shown only with "Include pre-releases" on)
- Main window behind the dialog and notice: the MVP sample data (MVP brief section 10), Disk 1 selected.
- For the "notice after an operation" case: success notification "Data (D:) was formatted as NTFS." followed by the update notice.

---

## 7. Out of scope (don't design)

- Downloading or installing updates from inside the app, progress for downloads, release notes inside the app.
- A settings page or other settings than the two switches.
- A custom installer UI, installer banners or wizard images.
- "What's new" screens after an update.

---

## 8. Design decisions (from the reviewed designs)

The designs ("Installer and updates" row of the WinParted design canvas) follow this brief. Details they settle:

- **About:** the app icon sits above the "WinParted" title. The repository link shows an "opens in browser" icon after the text. The "Restart WinParted as administrator to change these settings." line sits under the "Updates" heading, above the switches, with an info icon. In High Contrast, disabled labels and switches use GrayText (never dimmed by opacity) and the link uses the hyperlink color, underlined.
- **Screen readers:** a disabled switch is announced with its reason, e.g. "Include pre-releases, toggle switch, off, unavailable. Restart WinParted as administrator to change these settings."
- **Update notice:** an informational InfoBar with no title: one sentence, the **View release** button and Close (accessible name "Close update notice").
- **Order in the notifications strip:** newest notifications are on top, but the update notice always stays at the **bottom**: an operation's result appears above it, and errors always sit above it.
- **Notifications strip takes its own row:** the strip sits below the scrolling content and pushes it up instead of floating over it, so a notice that stays open never covers the layout bar or the actions at 1000 x 640. This applies to all notifications.

---

## 9. Deliverables requested from design

1. About dialog, light and dark: default state, checks off, without administrator rights.
2. About dialog in High Contrast (one theme is enough), showing the disabled switches.
3. Update notice, light and dark: full release, pre-release.
4. Main window at minimum size (1000 x 640) with the update notice shown, and with the update notice following an operation's success notification.
