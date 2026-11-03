# CLAUDE.md

This file guides Claude Code (or any AI coding assistant) when working in this repository. It is self-contained.

## Project

FocusCoach (working title, 業務作業集中コーチングアプリ) — a Windows desktop coaching tool that helps a software/system developer finish recurring, multi-step work tasks (e.g. writing a development plan, made of sub-tasks like "check the estimate", "check the outline spec") in less time. It guides each run using past runs of the same task type, switches the screen into a task-only workspace (minimise everything, restore afterwards), records sub-task times and the folders/files/URLs opened per sub-task, offers a timer, focus BGM, start/finish effects, and text/voice notes, and shows a post-task comparison with the previous run.

**Status: spec 01–09 done; version 0.14A.** Built so far: first build-out S1–S5 (interrupt F9/F10, away/lock/sleep handling, carry-over to next day, crash recovery), the UI restyle (spec 03 §3.4.1), "next step" / "first step" (I-01 / I-05), the task dropdown + steps editor + per-task drafts (empty start panel by default, spec 05 F-01), a portable self-contained build (`scripts/publish-portable.ps1`), the compact view (F8, semi-transparent click-through window anchored at the main window's top-left, spec 03 S-04b), tray minimize + app icon (spec 03 §3.5), and the interrupt exit flow — bookmark "割り込み対応" button, exit card, 40-second re-entry guide, persistent "あとでやること" list, themed dialogs instead of MessageBox (spec 03 §3.6, 09 §9.4.2b, design in `docs/review/design-0.12A-interrupt-closure.md`), and Japanese/English UI switching (0.13A: JSON string tables in `Core/Strings`, `Loc.T("key")`, `{loc:T key}` in XAML, JA/EN title-bar button + tray menu + Ctrl+Alt+F8; **never hard-code Japanese UI text — add the key to both ja.json and en.json**; a test scans for leftovers; spec 03 §3.7, 07 §7.5a; users stick to their main language — Japanese users type Japanese, possibly mixed with English; English users English only), and a SPIRE-style boot splash matching the author's Secrypt tool (0.14A, spec 03 §3.8; bundled OFL fonts in `src/FocusCoach.App/Fonts`). The main flows were verified from logs on the user's home Windows PC; what is still unverified is tracked in `docs/plan.md` 「要ローカル実機確認」. Resume point and next action: `docs/plan.md` — read it first. Run `git pull` before starting, so cloud and local sessions don't assign the same version number.

## Documents (Japanese)

- `docs/spec/` — **source of truth** for decisions. 01 overview, 02 components/environment, 03 UI, 04 data, 05 functions (F-01..F-17), 06 UX, 07 tech stack & phases (P-0..P-9), 08 debuggability/logging, 09 interruption recovery (F9/F10, away handling, resume card).
- `docs/concept.md` — the user's raw concept notes (input only; spec wins on conflict).
- `docs/plan.md` — progress checklist, current step, next action, gotchas.
- `docs/Version.md` — version history.
- `docs/review/` — reviews and records behind decisions: `review-0.4A.md` (code review, fix list R-xx, test items L-xx), `psychology-ideas.md` (enhancement ideas I-01..I-14), `realdevice-2026-10-04.md` (home-PC verification evidence). Keep plan.md short; put long investigations here.
- Each spec chapter ends with 未決事項 (open items, IDs like `U-05-2`); move an item into the body when decided.

## Planned stack (recommended, not yet locked — U-07-1)

C# / .NET 8. `FocusCoach.Core` (net8.0, OS-independent, event-fold state machine), `Storage` (SQLite + JSONL session logs), `Platform.Windows` / `Audio` / `App` (WPF) as net8.0-windows, `Cli` (scenario/replay/diag), xUnit tests. See `docs/spec/07-tech-stack.md`.

## Environment rules

- **Linux (no WPF):** you can build/test Core, Storage and Cli and run scenario files with `dotnet test FocusCoach.cloud.slnf` (the filter omits `FocusCoach.App`, because the Linux SDK has no WPF build targets; `FocusCoach.sln` includes it, so on Windows `dotnet build FocusCoach.sln` builds the WPF app too). `Platform.Windows` compiles on Linux (`EnableWindowsTargeting` is set in `Directory.Build.props`) but cannot run. XAML and the WPF code-behind cannot be verified on Linux. Never claim UI/OS behaviour was verified without running it on Windows.
- **Local (Windows):** full build/run. Set env var `FOCUSCOACH_DATA` to run the app against an isolated data folder instead of `%LOCALAPPDATA%\FocusCoach` (so trial runs don't pollute real history). Windows file sharing differs from Linux: any reader of a JSONL that a writer still holds open must use `FileShare.ReadWrite`; delete test files only after closing handles. Do not verify UI by sending synthetic keystrokes (focus stealing); use the CLI (`fc diag`, `fc workspace --dry-run`) or ask the user to check.
- Keep OS-dependent decision logic (e.g. which windows to minimise) in Core as pure functions so it is testable without Windows.
- Never force-kill other apps' windows; closing is `WM_CLOSE` only (spec F-10).
- Never commit real recorded data (DB, session JSONL, audio, logs) — it may contain confidential window titles/paths/URLs. Test data must be fictitious.

## Code layout and conventions

- `src/FocusCoach.Core` (pure; `Session` = command → event, `Reducer` = event fold, `MetricsCalculator`, `WorkspacePlanner`, scenario DSL), `src/FocusCoach.Storage` (JSONL + SQLite), `src/FocusCoach.Cli` (`fc`: scenario / replay / guide / workspace plan / diag), `src/FocusCoach.Platform.Windows` (Win32, monitors, `CoreHost`), `src/FocusCoach.App` (WPF mini panel), `tests/FocusCoach.Core.Tests` + `tests/scenarios/*.txt`.
- Core must stay OS-independent: do not use `System.IO.Path` on Windows-style paths (it is wrong on Linux); use `Reducer.FileNameOf`-style helpers.
- Every input goes through `CoreCommand`; state changes only in `Reducer.Apply`. Commands from OS monitors carry `origin=monitor` (their timestamps are clamped instead of rejected); do not use the `source` key for that (it is the resource capture method). New behaviour = new scenario file in `tests/scenarios/` + a test.
- Tentative decisions are marked in code as `[仮案-NN]` and listed in `docs/plan.md`. Grep `仮案-` before changing one; keep the numbering unique.

## Working approach

- Validate technical unknowns with throwaway spikes in `prototype/` before writing them into the spec.
- Spec first: when behaviour changes, update the relevant `docs/spec/0N-*.md` in the same change.
- Single-step sessions: implement one step → commit → update `docs/plan.md` (check off, set 現在のステップ / 次の作業, append gotchas). Conversation history is not the continuity mechanism; `docs/plan.md` is.

## Version management

- `docs/Version.md` is the source of truth (a `version` constant will be added with the code).
- Feature/improvement: bump the number (`0.1A` → `0.2A`); bug fix: bump the letter (`0.1A` → `0.1B`).
- Update `docs/Version.md` with every functional change, together with the affected spec chapter.

## Git

- Commit messages: short subject; mention the affected spec chapter when behaviour changes. Keep `docs/Version.md` and `AppInfo.Version` in sync.
