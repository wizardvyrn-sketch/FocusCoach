# FocusCoach

[日本語版 README](README.ja.md)

A Windows desktop app for routine work that spans several tools.

- It **breaks the routine into ordered sub-tasks with estimated times** (learned from your own past runs) and sets up an environment where you can concentrate on the work.
- It **helps you get back to work quickly after an interruption** and focus again.

When a colleague, a customer question or a chat message pulls you away from a task, the hard part is not the interruption itself - it is coming back: which windows were mine, where was I, what was the next step? FocusCoach saves that context before you leave and restores it when you return.

> **Status: prototype (version 0.14A).** It works end to end on the author's PCs, but it has had very little testing elsewhere. Please read [Known limitations](#known-limitations) first.

## What it does

- **Bookmark an interruption** with one key (`F9`) - no typing, no confirmation. Press it again when you are done.
- **Close the interruption** on a small card: pick a category, optionally write a subject, and if something is left over, jot it down as a *follow-up* with a due date. Pressing `Enter` closes it with nothing written.
- **Re-entry guide**: your original work window comes back to the front, with your own "next step" note and a short, calm countdown (40 s by default) before you are back in your task. What you do in those seconds is up to you.
- **Follow-ups list** that survives across tasks, stored locally.
- **Guided work sessions**: pick a past task, get a target time from your own history, track sub-steps, and compare with your previous runs.
- **Compact view** (`F8`): a small, translucent, click-through status strip that does not block the app underneath.
- **System tray**: minimize sends the app to the tray; the tray menu has the main actions.
- **Crash and next-day recovery**: an unfinished run can be resumed from where it stopped.
- **Japanese / English UI**: switch with the `JA`/`EN` button in the title bar, the tray menu, or `Ctrl+Alt+F8`.
- Screen sharing and recording **do not capture** the app's windows (task names and notes stay private).

Everything is stored **locally**. FocusCoach makes no network connections.

## Requirements

- Windows 10 or 11, 64-bit.
- The release ZIP is self-contained: nothing else to install, no administrator rights needed.

## Install (release ZIP)

1. Download `FocusCoach-<version>-win-x64.zip` and its `.sha256` file from the [Releases](../../releases) page.
2. Check the hash (PowerShell): `(Get-FileHash .\FocusCoach-<version>-win-x64.zip -Algorithm SHA256).Hash` and compare it with the `.sha256` file.
3. Unzip anywhere and double-click `FocusCoach.cmd`.
   `FocusCoach-portable.cmd` keeps all data in the folder's `data` subfolder instead (handy on a USB drive).

The executable is **not code-signed**, so Windows SmartScreen may warn you ("Windows protected your PC"). Choose *More info* -> *Run anyway* only if you trust the download and the hash matches. See `README.txt` inside the ZIP for details.

## Quick start

1. Type a task name (or pick a past one with the arrow), then press **Start focusing**.
2. When you are interrupted, press **`F9`** (or the bookmark button). Handle the interruption.
3. Press **`F9`** again. On the card, press **`Enter`** to close it (or note a follow-up first).
4. Your work window comes back; use the short guide to settle in, then carry on.

The app starts in **Practice mode** (it does not move or minimize your windows). Untick it only when you want the workspace features.

Hotkeys can be changed in `settings.json` (in the data folder). If another app already owns a key, FocusCoach says so and every action is still available by button.

## Build from source

Requirements: Windows, .NET 8 SDK.

```
dotnet build FocusCoach.sln
dotnet run --project src/FocusCoach.App
dotnet test FocusCoach.cloud.slnf        # unit tests and scenario tests (no UI)
```

To build the portable, self-contained folder: `scripts\publish-portable.ps1 -Dest <folder>`.

The code is organised so that nearly all behaviour is testable without a UI:

- `src/FocusCoach.Core` - OS-independent core: events, state, metrics, scenario DSL.
- `src/FocusCoach.Storage` - SQLite and JSONL session logs.
- `src/FocusCoach.Platform.Windows` - Win32 integration (window control, hotkeys, monitoring).
- `src/FocusCoach.App` - the WPF UI.
- `tests/` - xUnit tests and `tests/scenarios/*.txt` (a small text DSL that replays a work session headlessly).

UI strings live in `src/FocusCoach.Core/Strings/ja.json` and `en.json`; adding a language means adding a JSON file.

## Documentation

The specification and design notes are in **Japanese** (`docs/`). The UI is available in English, but the documents are not translated yet. Start at [docs/spec/01-system-overview.md](docs/spec/01-system-overview.md); the version history is in [docs/Version.md](docs/Version.md).

## Known limitations

- **Prototype quality.** It has been used on only a few Windows 11 PCs by one person. Expect rough edges.
- **Unsigned executable** (SmartScreen warning, see above).
- **Windows only** (WPF, Win32).
- **Workspace control** (minimizing/restoring other windows) is experimental; that is why Practice mode is on by default. The app never force-closes your windows.
- The system tray icon may be hidden under the `^` overflow arrow on Windows 11.
- Some planned features are **not implemented** (review screen polish, document launcher, background music, voice memos).
- The English text was written by an AI and may read unnaturally in places; corrections are welcome.
- Recorded data (window titles, file locations) is stored locally in plain form. Do not share the data folder or the logs.

## Built with AI

This project was developed with [Claude Code](https://claude.com/claude-code) (Anthropic), under the author's direction and review. The code, tests and documents were largely AI-generated.

## Security

See [SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE). Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
