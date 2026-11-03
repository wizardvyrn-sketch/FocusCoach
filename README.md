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
- The main windows, dialogs, compact view and guide cards **do not appear in screen sharing, recordings or screenshots** (task names and notes stay private). Exceptions: the short boot screen (it shows no task data) and the tray icon's menu and tooltip (status only). This needs Windows 10 version 2004 or later; on older builds the windows show up as black rectangles.

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

Press <kbd>F9</kbd> when you are interrupted, press it again when you are done. That is the whole idea. The screens below are rendered from the app with sample data.

<table>
<tr>
<td width="240" valign="top"><img src="docs/guide/images/en/01-start.png" width="220" alt="Start panel"></td>
<td valign="top">
<b>1. Start a task</b><br>
Type a task name, or pick a past one with the arrow. The steps and your "first step" note are filled in from last time, and the guide shows a target time learned from your history. Press <b>Start focusing</b>. The app starts in <b>Practice mode</b> (it does not move or minimize your windows); untick it only when you want the workspace features.
</td>
</tr>
<tr>
<td width="320" valign="top"><img src="docs/guide/images/en/02-running.png" width="300" alt="Mini panel"></td>
<td valign="top">
<b>2. Work with the mini panel</b><br>
It shows the current step and a timer against its target. <b>Done - next step</b> moves on; <b>Memo</b> (<kbd>F10</kbd>) jots down something for later without stopping the timer.
</td>
</tr>
<tr>
<td width="320" valign="top"><img src="docs/guide/images/en/03-bookmarked.png" width="300" alt="Bookmark placed"></td>
<td valign="top">
<b>3. An interruption arrives: press <kbd>F9</kbd></b><br>
One key press, no typing, no confirmation. The status changes to <i>Handling interruption</i> and the timer stops counting your work. Answer the question. If you want a hint for yourself, press <kbd>F10</kbd> and write where you were and your next step.
</td>
</tr>
<tr>
<td width="320" valign="top"><img src="docs/guide/images/en/04-exit-card.png" width="300" alt="Close card"></td>
<td valign="top">
<b>4. Done? Press <kbd>F9</kbd> again</b><br>
A small card opens. Just press <kbd>Enter</kbd> to close it with nothing written, or choose a category and, if something is left over, write it as a follow-up with a due date. <b>Tidy up windows</b> minimizes the windows the interruption opened (nothing is closed). <kbd>Esc</kbd> means "still handling it".
</td>
</tr>
<tr>
<td width="320" valign="top"><img src="docs/guide/images/en/05-reentry.png" width="300" alt="Re-entry guide"></td>
<td valign="top">
<b>5. Back to your task</b><br>
Your original work window comes back to the front, with your own "next step" note and a calm 40-second countdown. What you do in those seconds is up to you. <i>Back to work now</i> skips the wait; interruptions under 60 seconds skip the guide. It never takes the keyboard focus.
</td>
</tr>
<tr>
<td width="320" valign="top"><img src="docs/guide/images/en/06-followups.png" width="300" alt="Follow-ups list"></td>
<td valign="top">
<b>6. Follow-ups</b><br>
Everything you left over stays in a list that survives across tasks (sample data shown). Open it from the counter on the mini panel or the tray menu; check an item to mark it done.
</td>
</tr>
</table>

The full illustrated guide (English / Japanese, with the compact view, tray and hotkeys) is [docs/guide/index.html](docs/guide/index.html). GitHub shows HTML files as source, so clone or download the repository and open it in a browser.

| Key | Action |
|---|---|
| <kbd>F9</kbd> | Interruption bookmark / close it |
| <kbd>F10</kbd> | Memo (while working) / resume note (during an interruption) |
| <kbd>F8</kbd> | Compact view on / off |
| <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>F8</kbd> | Switch language (Japanese / English) |

Hotkeys can be changed in `settings.json` (see [Settings](#settings)). If another app already owns a key, FocusCoach says so and every action is still available by button.

## Settings

Settings are stored in `settings.json` in the [data folder](#where-your-data-is-stored). **Close FocusCoach before editing it** (the app rewrites the file when it exits). Key names are case-sensitive. If the file is missing or broken, the defaults are used. For example, to put the interruption bookmark on `F11`, use a 60-second guide and force English:

```json
{
  "HotkeyInterrupt": 122,
  "ReentryGuideSec": 60,
  "Language": "en"
}
```

| Key | Default | Meaning |
|---|---|---|
| `Language` | `"auto"` | `"auto"` (Japanese on a Japanese Windows, otherwise English), `"ja"` or `"en"` |
| `HotkeyInterrupt` | `120` (F9) | Interruption bookmark / close it. A virtual-key code, see below |
| `HotkeyMemo` | `121` (F10) | Memo while working, resume note during an interruption |
| `HotkeyCompact` | `119` (F8) | Compact view on / off |
| `HotkeyLanguage` | `119` (F8) | Key for switching language; `0` disables the hotkey |
| `HotkeyLanguageModifiers` | `3` (Ctrl+Alt) | Modifiers for the language hotkey, added together: Alt = 1, Ctrl = 2, Shift = 4 |
| `ReentryGuideSec` | `40` | Seconds the re-entry guide stays; `0` turns the guide off |
| `ReentryGuideMinInterruptSec` | `60` | Interruptions shorter than this skip the guide |
| `ReentryGuideMessage` | not set | Line of text in the guide; `{sec}` becomes the seconds; `""` hides it. Not set = a default per language |
| `CompactOpacity` | `0.55` | Compact view opacity, 0.1 to 1 |
| `CompactHoverOpacity` | `0.2` | Compact view opacity while the mouse is over it |
| `CompactClickThrough` | `true` | Clicks pass through the compact view (hold Ctrl to use its buttons) |
| `ExitCardTidyDefault` | `true` | Initial state of "Tidy up windows" on the close card |
| `DefaultTaskApps` | `"WINWORD.EXE,EXCEL.EXE"` | Initial value of "Apps used for this work" |
| `WorkspaceDryRun` | `true` | Practice mode: do not move or minimize windows (also the checkbox on the start panel) |

The app also keeps `PanelLeft`, `PanelTop`, `TrayHintShown`, `ReentryGuideHintShown` and `LastInterruptCategory` in this file. Leave those alone.

Hotkey values are Windows **virtual-key codes as decimal numbers**: `F1` to `F12` are `112` to `123` (so `F9` is `120`), `F13` to `F24` are `124` to `135`, letters `A` to `Z` are `65` to `90`, digits `0` to `9` are `48` to `57`. Other keys: see Microsoft's "Virtual-Key Codes" list. The three main hotkeys are registered **globally without modifiers**: while FocusCoach runs, that key stops working in other apps (for example `F9` recalculates in Excel). Pick keys other apps do not need. If a key cannot be registered, FocusCoach tells you and every action is still available by button.

Two environment variables are useful for developers: `FOCUSCOACH_DATA` (use another data folder) and `FOCUSCOACH_ALLOW_CAPTURE=1` (turn off the screen-capture exclusion so that you can take screenshots).

## Where your data is stored

| Launcher | Data folder |
|---|---|
| `FocusCoach.cmd` (or `app\FocusCoach.exe`) | `%LOCALAPPDATA%\FocusCoach` (per Windows user, per PC) |
| `FocusCoach-portable.cmd` | the `data` folder next to the launcher |
| either, with `FOCUSCOACH_DATA` set | that folder |

| Inside the data folder | Content |
|---|---|
| `focuscoach.db` | SQLite database: tasks, runs, steps, follow-ups, interruption records |
| `sessions\*.jsonl` | one event log per run; an unfinished or suspended run is resumed from here |
| `sessions\discarded\` | runs you chose to discard (moved here, not deleted) |
| `logs\app-YYYYMMDD.log`, `logs\crash-*.txt` | application log (kept 14 days) and crash reports (kept 30 days) |
| `settings.json` | your settings (see above) |

Nothing is encrypted, and window titles and file locations can be recorded. Do not share this folder or the logs as they are.

## Uninstall

1. Quit FocusCoach (tray icon -> *Quit FocusCoach*, or the X button).
2. Delete the folder you unzipped. There is no installer: **no registry entries, no startup entry and no shortcuts** are created.
3. Your data lives in the data folder (see above), not in the program folder. To erase it too, delete `%LOCALAPPDATA%\FocusCoach` (standard launcher) or the `data` folder (portable launcher). Copy it elsewhere first if you may want it later.

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

UI strings live in `src/FocusCoach.Core/Strings/ja.json` and `en.json`. Adding a language takes a new JSON file **and** a few code changes (see [CONTRIBUTING.md](CONTRIBUTING.md#adding-a-language)): the title-bar button, the tray menu and `Ctrl+Alt+F8` currently toggle between Japanese and English only.

### Command-line tool (`fc`)

`src/FocusCoach.Cli` replays work sessions **without any UI** - handy for checking a change or reproducing a bug. Run it with `dotnet run --project src/FocusCoach.Cli -- <command>` (works on any OS; some of its output text is in Japanese).

| Command | What it does |
|---|---|
| `scenario <file> [--events out.jsonl] [--db out.db] [-v]` | Runs a scenario script in virtual time, checks its `expect` lines and prints the metrics. Exit code `1` if a check fails. `--events` writes the event log, `--db` saves the run to a database. |
| `replay <session.jsonl> [-v]` | Recomputes state and metrics from a recorded event log (also works on files in `sessions\`). |
| `guide --db <db> --category <name>` | Prints the pre-task guide (target times) built from past runs. |
| `workspace plan <windows.json> --mode start\|resume\|finish [--snapshot <windows.json>] [--project <name>]` | Lists which windows would be minimized, restored or closed. A dry run: nothing is touched. |
| `diag [--data-root <dir>]` | Environment diagnostics. Prints no personal data. |

A scenario is a text file: `at <hh:mm[:ss]> <command> key=value ...` runs a command at that virtual time, `expect <path> <op> <value>` checks the state (`==`, `!=`, `~=`, `<`, `<=`, `>`, `>=`; durations like `12m` or `1h30m`), `!<command>` means the command must be rejected, `#` starts a comment.

```
# my-first.txt: one interruption of 12 minutes
at 00:00 start_task category="Design review" subtasks="Review estimate,Check outline"
at 00:10 interrupt kind=now
expect paused == yes
at 00:22 interrupt_close
expect paused == no
at 01:00 end_task
expect metrics.interrupt_count == 1
expect metrics.interrupt_paused_sec == 12m
```

```
dotnet run --project src/FocusCoach.Cli -- scenario my-first.txt -v --events out.jsonl
dotnet run --project src/FocusCoach.Cli -- replay out.jsonl
```

More examples are in `tests/scenarios/`; the command and path reference is in `docs/spec/08-debug-logging.md` (Japanese).

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

## Contributing

Bug reports, English wording fixes and focused pull requests are welcome; please read [CONTRIBUTING.md](CONTRIBUTING.md) first (replies are best effort).

## Security

See [SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE). Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
