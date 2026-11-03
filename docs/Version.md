# Version

This file is the source of truth for the version history. / 本ファイルを更新履歴の正とする。

## Rules / ルール

- New feature or improvement: bump the number (`0.1A` → `0.2A`). / 機能追加・改善: 番号を上げる
- Bug fix: bump the letter (`0.1A` → `0.1B`). / バグ修正: 末尾のレターを上げる
- When a change affects behaviour, update the matching `docs/spec/0N-*.md` in the same change. / 仕様に影響する変更は、該当する仕様書も同じ変更で更新する

## History / 履歴

The history below is summarized; the development history before the first public release is not part of this repository. / 以下は要約。公開前の開発の履歴はこのリポジトリには含まれない。

| Version | Date | Summary | 概要 |
|---------|------|---------|------|
| 0.0A – 0.3A | 2026-10-03 | Specification written (chapters 01–09): concept, UI, data, functions, UX, tech stack, debugging and interruption-recovery design. | 仕様書(01〜09章)を作成。 |
| 0.4A | 2026-10-03 | First prototype: OS-independent core (event-sourced session state, metrics), SQLite and JSONL storage, CLI, scenario DSL for headless replay, simple WPF panel. | 第一プロトタイプ。中核・保存・CLI・シナリオ・簡易パネル。 |
| 0.5A – 0.5C | 2026-10-04 | Interruption handling (bookmark with `F9`, memo with `F10`), recovery after being away / lock / sleep, carry-over to the next day, crash recovery, hidden from screen sharing. Bug fixes from the first real-device checks. | 割り込み対応、離席・ロックからの復帰、持ち越し、異常終了からの復帰、画面共有からの除外。実機確認での修正。 |
| 0.6A | 2026-10-04 | UI redesign: calm futuristic dark theme in teal and blue, readable fonts, short wording. | 画面デザインの刷新(近未来・低刺激の配色)。 |
| 0.7A – 0.7B | 2026-10-04 | "Next step" and "first step" notes shown on the resume card; the app log records the running version. | 「次の一手」「最初の一手」。起動時にバージョンをログへ。 |
| 0.8A | 2026-10-04 | Task dropdown filled from past tasks; sub-step editor. | 過去のタスクから選ぶプルダウン、手順エディタ。 |
| 0.9A | 2026-10-04 | Portable, self-contained build (no .NET install needed). | 持ち運び版(自己完結)。 |
| 0.10A | 2026-10-06 | Empty start panel, drafts saved per task, standard-size title buttons, "Finish task" wording, compact view (`F8`: translucent, click-through). | 空から始める、入力の下書き、タイトル帯の見直し、コンパクト表示。 |
| 0.11A | 2026-10-10 | Compact view anchored at the window's top-left, minimize to the system tray, app icon, second launch brings the running app forward. | コンパクト表示の位置、トレイへの格納、アイコン、2つ目の起動で前面へ。 |
| 0.12A | 2026-10-29 | Interruption flow: bookmark button, exit card (category, follow-up), 40-second re-entry guide, persistent follow-ups list, themed dialogs. | 割り込み対応ボタン、出口カード、再入場ガイド、あとでやること、アプリと同じ見た目のダイアログ。 |
| 0.13A | 2026-11-03 | Japanese / English UI with a title-bar switch, tray menu and `Ctrl+Alt+F8`; string tables in `src/FocusCoach.Core/Strings`. | 日本語 / 英語の表示切り替え。 |
| 0.14A | 2026-11-03 | Boot screen; fixes from a code review (app exit with the follow-ups list open, exit-card button handling, language handling). First public release. | 起動画面、コードレビューでの修正。初の公開版。 |
