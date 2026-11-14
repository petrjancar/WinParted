# Instructions for coding agents

WinParted is a Windows 11 disk and partition manager (C#, WinUI 3, .NET 10). Building, running, releasing and the project layout are in [CONTRIBUTING.md](CONTRIBUTING.md). This file lists the rules that the code doesn't show.

## Safety first

- **Never run anything that changes a real disk.** No disk operations against physical disks, no `diskpart`, no `Format-Volume` or similar on real drives.
- Integration tests (`tests/WinParted.Storage.IntegrationTests`) create temporary VHDX files and touch only those. They need an elevated terminal; leave them to the developer unless asked.
- To try the UI, use **sample mode** (`--sample`): example disks, nothing is changed.

## Architecture rules

- `WinParted.Core` stays pure .NET: no Windows APIs, so all rules are unit tested. New logic goes there, with tests in `tests/WinParted.Core.Tests`.
- All user-facing text goes in `src/WinParted.Core/Resources/Texts.resx`.
- The app is English-only. Set texts explicitly instead of relying on WinUI control defaults, which follow the Windows language.
- Warnings are errors (`TreatWarningsAsErrors`).
- WinParted runs elevated: open web pages through `UpdateService.OpenInBrowser` (via Explorer), never directly, so the browser doesn't run as administrator.

## Build gotchas

- Keep `EnableMsixTooling` in `WinParted.App.csproj`: without it, publish leaves out `WinParted.pri` and the app crashes at start.
- Reference `Microsoft.WindowsAppSDK.WinUI` and `Microsoft.WindowsAppSDK.Runtime`, not the `Microsoft.WindowsAppSDK` metapackage (it adds ~50 MB of unused AI/ML runtimes).
- The version is set only in `Directory.Build.props` (`<Version>`).

## Workflow

- New features start as an **OpenSpec change** (`openspec/changes/`): proposal, specs, design, tasks, reviewed before code. `openspec/specs/` is the source of truth for behavior.
- Commit, push or open pull requests only when asked.
- Work on a branch from an up-to-date `main`. Before pushing to an existing branch, check that its pull request is still open; never push to a branch whose pull request was merged.
- `main` is protected: changes go through a pull request, and the `build` check must pass.
- A change isn't ready for review until its docs are updated in the same pull request: README.md (features, limitations, download), CONTRIBUTING.md (build, run, release, developer options), `docs/design/` briefs (UI wording or behavior), and the README screenshots if the main window changed (retake them with `pwsh docs/images/make-screenshots.ps1`).

## Checking UI changes

- Run the app in sample mode; Debug builds start without administrator rights, which is fine for sample mode.
- Run a Release build without elevation by setting `__COMPAT_LAYER=RunAsInvoker`.
- `--pretend-version <x.y.z>` makes the update check run (even without administrator rights) and compare GitHub's releases with that version.
- `--theme`, `--culture en-US`, `--select` and `--open` put the app in a given state for screenshots (see CONTRIBUTING.md, "Running").
