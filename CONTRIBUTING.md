# Contributing to FocusCoach

Thank you for your interest! FocusCoach is a small prototype maintained by one person in spare time, largely generated with an AI coding assistant under the maintainer's direction. **Replies and merges are best effort, with no guaranteed response time.** Small, focused contributions are the easiest to review. 日本語の要約は末尾にあります。

## Ways to help

- **Bug reports** (open an issue): version (see `VERSION.txt` in the ZIP, or the first line of `logs/app-*.log`), Windows version, what you did, what you expected, what happened.
  **Do not paste your data folder or logs as they are.** They can contain window titles and file paths; redact them first.
- **English wording fixes.** The English UI text was written by an AI and may read unnaturally. Edit `src/FocusCoach.Core/Strings/en.json` and send a pull request.
- **Security problems:** do not open a public issue; see [SECURITY.md](SECURITY.md).
- **Ideas and features:** open an issue first, so we can check that it fits the design before you spend time on it.

## Set up

Requirements: **.NET 8 SDK**. The UI needs **Windows** (WPF, Win32). Everything else builds and tests on Linux too (macOS is untested).

```
dotnet build FocusCoach.sln                  # Windows: everything, including the WPF app
dotnet test FocusCoach.cloud.slnf            # any OS: core, storage, CLI, tests (no UI project)
dotnet run --project src/FocusCoach.App      # Windows: run the app
```

(`FocusCoach.cloud.slnf` is simply the solution filter without the WPF project.) To run the app against a throw-away data folder instead of your real one, set `FOCUSCOACH_DATA` to an empty folder first.
The command-line tool `fc` replays work sessions without any UI; see "Command-line tool" in the [README](README.md).

## How the code is organised

| Project | Role |
|---|---|
| `src/FocusCoach.Core` | OS-independent core: `Session` turns commands into events, `Reducer` folds events into state, metrics, guides, the scenario DSL, string tables (`Strings/*.json`). **No UI, no Windows APIs.** |
| `src/FocusCoach.Storage` | SQLite database and JSONL session logs, settings file |
| `src/FocusCoach.Platform.Windows` | Win32 integration: window control, hotkeys, activity monitors, `CoreHost` (glue between UI and core) |
| `src/FocusCoach.App` | The WPF UI |
| `src/FocusCoach.Cli` | `fc`: run scenarios, replay logs, diagnostics |
| `tests/` | xUnit tests and `tests/scenarios/*.txt` (text scripts that replay a work session) |

## Rules of the road

1. **Keep decisions in the core and testable.** Every input goes through a `CoreCommand`; state changes only in `Reducer.Apply`. Put OS-dependent decision logic (e.g. which windows to minimize) in Core as pure functions, not in the UI.
2. **Test it.** Add a unit test or, better, a scenario in `tests/scenarios/` (`at 00:10 interrupt kind=now`, `expect paused == yes`). Run `dotnet test FocusCoach.cloud.slnf` before sending a pull request; all tests must pass.
3. **UI text goes through the string tables.** Never hard-code UI text. Add the key to **both** `src/FocusCoach.Core/Strings/ja.json` and `en.json` and use `Loc.T("key", args)` (code) or `{loc:T key}` (XAML). A test fails if the two files differ in keys or placeholders, or if Japanese text is hard-coded in the source.
4. **The specification is in Japanese and is the source of truth** (`docs/spec/`). If you change behaviour, update the matching chapter in the same pull request (English is fine; it will be translated). Open questions are listed at the end of each chapter as `U-nn-n`.
5. **Versions:** a feature or improvement bumps the number (`0.14A` -> `0.15A`), a bug fix bumps the letter (`0.14A` -> `0.14B`). Update `docs/Version.md` and `src/FocusCoach.Core/AppInfo.cs` together if the maintainer asks you to; otherwise leave it to them.
6. **Never commit real data**: no `*.db`, session logs, `logs/`, screenshots of your real windows. Test data must be fictitious.
7. **Be gentle with other apps**: the app must never force-kill or close other windows without the user choosing (closing is `WM_CLOSE` only), and must not capture screens, keystrokes or window contents beyond what the specification lists.
8. Match the style of the surrounding code. Comments may be Japanese or English.

### Adding a language

1. Create `src/FocusCoach.Core/Strings/<code>.json` with the same keys as `en.json` (the test checks keys and `{0}` placeholders).
2. Add the code to `Loc.Languages` in `src/FocusCoach.Core/Loc.cs`.
3. The title-bar button, the tray menu and `Ctrl+Alt+F8` currently **toggle between Japanese and English only** (`Loc.Next`, `TrayIcon`), so a third language also needs a way to be chosen (a menu entry or a setting).

## Pull requests

- One topic per pull request; describe what changed and why, and how you checked it (tests, or what you tried on Windows).
- Say so if you used an AI assistant; it is welcome, but you are responsible for the result.
- By contributing you agree that your contribution is licensed under the [MIT License](LICENSE) like the rest of the project.

---

## 日本語の要約

個人が空き時間に作っている試作で、AI の支援で作った部分が大きく、**返信や反映には時間がかかります(保証なし)**。

- **不具合の報告**: 版、Windows の版、手順、期待した動き、実際の動きを書いてください。**データのフォルダやログは、そのまま貼らないでください**(ウィンドウのタイトルやファイルの場所が含まれます)。
- **英語の文言の修正**は歓迎です(`src/FocusCoach.Core/Strings/en.json`)。脆弱性は公開の Issue ではなく [SECURITY.md](SECURITY.md) へ。
- **準備**: .NET 8 SDK。画面は Windows 専用ですが、中核・保存・CLI・テストは他の OS でも動かせます(`dotnet test FocusCoach.cloud.slnf`)。
- **決まり**: 判断は Core に置いてテストする(シナリオを書くのが近道)/ 画面の文言は `ja.json` と `en.json` の両方に足して `Loc.T` で引く(直書きするとテストが落ちます)/ **仕様書(`docs/spec/`、日本語)が正**で、挙動を変えたら同じ変更で直す(英語で書いてもかまいません)/ 実際の記録データは決してコミットしない。
- **言語の追加**: JSON を足し、`Loc.Languages` に加えます。ただし切り替えのボタン・トレイ・Ctrl+Alt+F8 は日本語 ⇄ English の 2 つ固定なので、3 つ目の言語には選ぶ方法も要ります。
- Pull request は 1 件 1 テーマで、確認の方法(テスト、Windows で試したこと)を書いてください。貢献は MIT ライセンスで提供されたものとします。
