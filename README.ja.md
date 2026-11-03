# FocusCoach

[English README](README.md)

複数のツールを使うルーチンワークを、**サブタスクの順序と予想時間に分解**し、作業に集中できる環境を作る Windows アプリ。
作業の割り込みが入ったときに、**元の作業へ素早く戻って、再び集中できる**ように支援する。

Windows PC での定型業務(例:開発計画書の作成)を、過去の同種タスクの実績をもとにガイドする。
主眼は、割り込みで中断した作業へ短時間で戻れるようにすること(中断の前に文脈を退避し、戻るときに復元する)。
ほかに、タスク専用ワークスペース、サブタスクごとの時間と参照資料の自動記録、前回との比較による振り返りを備える。

## 現在の状態

**プロトタイプ(0.14A)。** 開始準備(空から始まる、過去のタスクから選ぶプルダウン、手順の登録、入力の下書き保存)と、
作業中のミニパネル(割り込み F9 / メモ F10、離席・ロックの問いかけ、復帰カード、次の一手・最初の一手、
異常終了・翌日からの再開、ワークスペースの最小化と復元)、コンパクト表示(F8。元のウィンドウの左上を起点に縮み、半透明で、下のアプリをクリックできる)、最小化でタスクトレイに格納、アプリのアイコンが動く・付く。0.12A で、割り込み対応(しおり)→ 出口カードで閉じる → 40秒の再入場ガイドで元の作業へ、の流れと、タスクをまたいで残る「あとでやること」、アプリと同じ見た目のダイアログを追加。0.13A で、**英語表示**(日本語 ⇄ English)に対応した(タイトル帯の JA / EN ボタン、トレイのメニュー、Ctrl+Alt+F8)。0.14A で、同じ作者の Secrypt と同じ SPIRE デザインの起動画面を追加。
自宅PCで主な操作を確認済み。振り返りの画面、資料ランチャー、BGM・演出・音声メモは未実装。進捗と次の作業は [docs/plan.md](docs/plan.md) を参照。

## 使い方

割り込まれたら <kbd>F9</kbd>、終わったらもう一度 <kbd>F9</kbd>。これが基本です。下の画面は、サンプルデータでアプリから描画したものです。

<table>
<tr>
<td width="240" valign="top"><img src="docs/guide/images/ja/01-start.png" width="220" alt="開始準備"></td>
<td valign="top">
<b>1. タスクを始める</b><br>
タスク名を入力するか、▾ で過去のタスクを選びます。手順と「最初の一手」が前回の内容で入り、ガイドには過去の実績から出した目標時間が出ます。「集中を始める」を押します。最初は<b>練習モード</b>(ウィンドウを動かさない)です。作業用ウィンドウの整理を使うときだけ、チェックを外してください。
</td>
</tr>
<tr>
<td width="320" valign="top"><img src="docs/guide/images/ja/02-running.png" width="300" alt="ミニパネル"></td>
<td valign="top">
<b>2. ミニパネルで作業する</b><br>
今の手順と、目安に対するタイマーが出ます。「完了して次へ」で進み、「メモ」(<kbd>F10</kbd>)で、タイマーを止めずにあとで対応することを書き留められます。
</td>
</tr>
<tr>
<td width="320" valign="top"><img src="docs/guide/images/ja/03-bookmarked.png" width="300" alt="しおりを挟んだ状態"></td>
<td valign="top">
<b>3. 割り込みが来たら <kbd>F9</kbd></b><br>
キーを 1 回押すだけ。入力も確認もありません。状態が「割り込み対応中」になり、作業時間の計測が止まります。そのまま対応してください。自分へのヒントを残すなら <kbd>F10</kbd> で、どこまで進んだかと次の一手を書きます。
</td>
</tr>
<tr>
<td width="320" valign="top"><img src="docs/guide/images/ja/04-exit-card.png" width="300" alt="出口カード"></td>
<td valign="top">
<b>4. 終わったらもう一度 <kbd>F9</kbd></b><br>
小さなカードが開きます。何も書かずに <kbd>Enter</kbd> で閉じられます。カテゴリを選び、残ったことは期限つきで 1 行書きます。「割り込みで開いたウィンドウを片付ける」は、割り込みで開いたウィンドウを最小化します(閉じません)。<kbd>Esc</kbd> は「まだ対応を続ける」です。
</td>
</tr>
<tr>
<td width="320" valign="top"><img src="docs/guide/images/ja/05-reentry.png" width="300" alt="再入場ガイド"></td>
<td valign="top">
<b>5. 元の作業へ</b><br>
元の作業ウィンドウが前に出て、自分で書いた「次の一手」と、落ち着いた 40 秒のカウントダウンが出ます。この間に何をするかは自由です。「今すぐ戻る」で待たずに戻れ、60 秒未満の割り込みでは出ません。キーボードのフォーカスは奪いません。
</td>
</tr>
<tr>
<td width="320" valign="top"><img src="docs/guide/images/ja/06-followups.png" width="300" alt="あとでやること一覧"></td>
<td valign="top">
<b>6. あとでやること</b><br>
残したものは、タスクをまたいで残る一覧に入ります(画面はサンプル)。ミニパネルの件数かトレイのメニューから開き、チェックで完了にします。
</td>
</tr>
</table>

画像つきの詳しい操作ガイド(日本語 / English。コンパクト表示・トレイ・キーを含む)は [docs/guide/index.html](docs/guide/index.html) です。GitHub では HTML がソースとして表示されるため、リポジトリを取得して、ブラウザで開いてください。

| キー | 動作 |
|---|---|
| <kbd>F9</kbd> | 割り込みのしおり / 割り込みを閉じる |
| <kbd>F10</kbd> | メモ(作業中)/ 再開メモ(割り込み中) |
| <kbd>F8</kbd> | コンパクト表示の切り替え |
| <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>F8</kbd> | 表示言語の切り替え(日本語 / English) |

キーは `settings.json`(データのフォルダ内)で変えられます。ほかのアプリが同じキーを使っているときは画面でお知らせします。ボタンでも同じ操作ができます。

## 動かし方(Windows)

```
dotnet build FocusCoach.sln
dotnet run --project src/FocusCoach.App
```

最初は「練習モード」(ウィンドウを動かさない)になっている。テストは `dotnet test FocusCoach.cloud.slnf`(画面のプロジェクトを除いたもの。どの OS でも動く)。
画面なしで作業を再現できるコマンドラインツール `fc` の使い方は、英語の README の「Command-line tool (`fc`)」にある(コマンドの一覧、シナリオの書き方と動く例)。詳細は [docs/spec/08-debug-logging.md](docs/spec/08-debug-logging.md)。

### 他の端末で使う(持ち運び版)

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish-portable.ps1 -Dest E:\FocusCoach
```

.NET の実行環境ごと同梱するので、配布先の Windows 10/11 (64bit) には何もインストールしなくてよい(約 160MB)。使い方は [deploy/README.txt](deploy/README.txt)。

## 設定(settings.json)

設定は、[記録の場所](#記録の場所)にある `settings.json` に保存されます。**編集するときは、先に FocusCoach を閉じてください**(終了時にアプリがファイルを書き直します)。項目の名前は大文字・小文字を区別します。ファイルが無い・壊れているときは、既定値で動きます。例: 割り込みのしおりを `F11` にし、ガイドを 60 秒にし、表示を英語に固定する。

```json
{
  "HotkeyInterrupt": 122,
  "ReentryGuideSec": 60,
  "Language": "en"
}
```

| 項目 | 既定 | 内容 |
|---|---|---|
| `Language` | `"auto"` | `"auto"`(日本語の Windows なら日本語、それ以外は英語)/ `"ja"` / `"en"` |
| `HotkeyInterrupt` | `120`(F9) | 割り込みのしおり / 割り込みを閉じる。仮想キーコード(下記) |
| `HotkeyMemo` | `121`(F10) | 作業中はメモ、割り込み中は再開メモ |
| `HotkeyCompact` | `119`(F8) | コンパクト表示の切り替え |
| `HotkeyLanguage` | `119`(F8) | 表示言語を切り替えるキー。`0` で無効 |
| `HotkeyLanguageModifiers` | `3`(Ctrl+Alt) | 言語のホットキーの修飾キー。足し合わせる: Alt = 1、Ctrl = 2、Shift = 4 |
| `ReentryGuideSec` | `40` | 再入場ガイドを出す秒数。`0` で出さない |
| `ReentryGuideMinInterruptSec` | `60` | これより短い割り込みでは、ガイドを出さない(秒) |
| `ReentryGuideMessage` | 未設定 | ガイドの一言。`{sec}` は秒数。`""` で出さない。未設定なら言語ごとの既定文 |
| `CompactOpacity` | `0.55` | コンパクト表示の不透明度(0.1〜1) |
| `CompactHoverOpacity` | `0.2` | コンパクト表示にマウスを重ねたときの不透明度 |
| `CompactClickThrough` | `true` | コンパクト表示の上のクリックを下のアプリに通す(Ctrl を押している間だけ触れる) |
| `ExitCardTidyDefault` | `true` | 出口カードの「割り込みで開いたウィンドウを片付ける」の初期値 |
| `DefaultTaskApps` | `"WINWORD.EXE,EXCEL.EXE"` | 「作業で使うアプリ」の初期値 |
| `WorkspaceDryRun` | `true` | 練習モード(ウィンドウを動かさない)。開始準備のチェックと同じもの |

このファイルには `PanelLeft`・`PanelTop`・`TrayHintShown`・`ReentryGuideHintShown`・`LastInterruptCategory` も、アプリが記録します。これらは触らないでください。

ホットキーの値は、Windows の**仮想キーコードの 10 進数**です。`F1`〜`F12` は `112`〜`123`(`F9` は `120`)、`F13`〜`F24` は `124`〜`135`、英字 `A`〜`Z` は `65`〜`90`、数字 `0`〜`9` は `48`〜`57` です。ほかのキーは、Microsoft の「Virtual-Key Codes」の一覧を見てください。3 つの主なホットキーは、**修飾キーなしでグローバルに登録**されます。FocusCoach の起動中は、そのキーがほかのアプリで効かなくなります(例: Excel の `F9` = 再計算)。ほかのアプリが使わないキーを選んでください。登録できないときは画面でお知らせし、ボタンでも同じ操作ができます。

開発者向けの環境変数が 2 つあります。`FOCUSCOACH_DATA`(別の記録の保存先を使う)と、`FOCUSCOACH_ALLOW_CAPTURE=1`(画面の撮影からの除外を外して、スクリーンショットを撮れるようにする)。

## 記録の場所

| 起動の方法 | 記録のフォルダ |
|---|---|
| `FocusCoach.cmd`(または `app\FocusCoach.exe`) | `%LOCALAPPDATA%\FocusCoach`(Windows のユーザーごと、PC ごと) |
| `FocusCoach-portable.cmd` | 起動用ファイルの隣の `data` フォルダ |
| どちらでも、`FOCUSCOACH_DATA` を設定したとき | そのフォルダ |

| 記録のフォルダの中 | 内容 |
|---|---|
| `focuscoach.db` | SQLite のデータベース: タスク、実行、手順、あとでやること、割り込みの記録 |
| `sessions\*.jsonl` | 実行ごとのイベントの記録。途中で終わった・中断した実行は、ここから再開する |
| `sessions\discarded\` | 「破棄」を選んだ実行(削除ではなく、ここへ移す) |
| `logs\app-YYYYMMDD.log`、`logs\crash-*.txt` | アプリのログ(14 日間保存)と、異常終了の記録(30 日間保存) |
| `settings.json` | 設定(上記) |

暗号化はしていません。ウィンドウのタイトルやファイルの場所が記録されることがあります。このフォルダやログは、そのままの形で他人に渡さないでください。

## アンインストール

1. FocusCoach を終了します(タスクトレイのアイコン →「アプリを閉じる」、または ✕ ボタン)。
2. 展開したフォルダを削除します。インストーラーは使わないので、**レジストリ・スタートアップ・ショートカットは作られていません**。
3. 記録は、プログラムのフォルダではなく、記録のフォルダ(上記)にあります。記録も消すときは、`%LOCALAPPDATA%\FocusCoach`(通常の起動)または `data` フォルダ(持ち運び用の起動)を削除します。あとで使うかもしれないときは、先に別の場所へコピーしてください。

## 仕様書

章立てごとに `docs/spec/` 以下に分割している。読む順序は番号順を推奨。

1. [01-system-overview.md](docs/spec/01-system-overview.md) — システム概略
2. [02-components-environment.md](docs/spec/02-components-environment.md) — 構成要素・動作環境
3. [03-ui.md](docs/spec/03-ui.md) — UI仕様(画面のデザイン方針は §3.4.1)
4. [04-data.md](docs/spec/04-data.md) — データ仕様
5. [05-functions.md](docs/spec/05-functions.md) — 機能仕様
6. [06-ux.md](docs/spec/06-ux.md) — UX仕様
7. [07-tech-stack.md](docs/spec/07-tech-stack.md) — 技術選定・実装方針、実装状況
8. [08-debug-logging.md](docs/spec/08-debug-logging.md) — デバッグ性を考慮した設計、ログ設計方針
9. [09-focus-advice.md](docs/spec/09-focus-advice.md) — 中断からの復帰支援(旧: 集中切れアドバイスポップアップ)

## フォルダ構成

- `src/` — FocusCoach.Core(OS に依存しない中核)、Storage(SQLite・JSONL)、Platform.Windows(Win32 連携)、App(WPF の画面)、Cli(`fc`: シナリオ実行・リプレイ・診断)
- `tests/` — xUnit のテストと、`tests/scenarios/*.txt`(シナリオDSL)
- `docs/concept.md` — 構想イメージ原文(仕様の出発点。仕様と矛盾する場合は `docs/spec/` が正)
- `docs/spec/` — 仕様書本体(決定事項の正)
- `docs/plan.md` — 進捗、再開ポイント、次の作業
- `docs/review/` — レビュー、強化案、実機確認の記録
- `docs/Version.md` — バージョン履歴
- `CLAUDE.md` — Claude Code 向けのプロジェクト説明と作業ルール
- `deploy/` — 持ち運び版に同梱するファイル(起動用 .cmd と README)。`scripts/publish-portable.ps1` が使う
- `prototype/` — 画面の見た目の確認(`ui_render_probe`)とアイコン生成(`icon_gen`)の開発用ツール

## 貢献

不具合の報告、英語の文言の修正、小さな Pull request を歓迎します。先に [CONTRIBUTING.md](CONTRIBUTING.md)(末尾に日本語の要約あり)を読んでください。返信や反映には時間がかかります(保証なし)。

## License

[MIT](LICENSE)。第三者の素材は [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) にまとめている。

## Version

[docs/Version.md](docs/Version.md) 参照。

## 第三者の素材

- 起動画面の書体 [Oxanium](https://github.com/sevmeyer/oxanium) と [Share Tech Mono](https://fonts.google.com/specimen/Share+Tech+Mono) は SIL Open Font License 1.1 で同梱している。ライセンス文は `src/FocusCoach.App/Fonts/*-OFL.txt`(配布物では `app\Fonts\`)。
