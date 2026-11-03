# 07. 技術選定・実装方針

> 状態: 初版(2026-10-03)。技術スタックは推奨案(U-07-1)のまま、第一プロトタイプ(0.4A)を実装した。構成と実装状況は §7.6。

## 7.1 推奨: C# / .NET 8 + WPF

### 比較

| 観点 | **.NET 8 + WPF**(推奨) | Tauri 2 + React | Electron | Python + PySide6 |
|------|------------------------|-----------------|----------|-------------------|
| Win32/COM/UI Automation | ◎ P/Invokeと `dynamic` COM が標準でそろう。UIA は `System.Windows.Automation` | △ Rust の `windows` crate で可能だが、COM(Shell.Application)は記述が重い | △ ネイティブアドオンが必要 | ○ pywin32 で可能 |
| 常駐時のメモリ・CPU | ○ | ◎ | × 常駐用途には重い | ○ |
| 演出の表現力 | ○ Storyboard/Composition。必要に応じてWebView2を併用できる | ◎ CSS/Canvas | ◎ | △ |
| 音声(録音/再生/whisper) | ◎ NAudio、Whisper.net | ○ | ○ | ◎ |
| 配布(管理者権限なし) | ◎ self-contained の単一フォルダ | ◎ | ○ | △ 同梱が煩雑 |
| クラウドセッション(Linux)での開発 | ○ Core は net8.0 でビルド・テストできる。WPF は `EnableWindowsTargeting` でビルドのみ可能 | ○ フロントエンドのみ | ○ | ○ |
| 既存の知見 | NasBackupTool(.NET 8 WPF)と同じ構成 | StickyNotes と同じ構成 | — | ConductorRhythmGame |

本ツールの中心はOS連携(ウィンドウのスナップショットと復元、Explorer COM、イベントフック、アイドル検知)であり、この部分の実装量と安定性が品質を決める。そのため、Win32/COM を最も素直に扱える .NET 8 + WPF を推奨する。演出の表現力が足りない場合は、そこだけ WebView2 で HTML/CSS アニメーションを重ねる。

### 主要ライブラリ

| 用途 | ライブラリ |
|------|-----------|
| MVVM | CommunityToolkit.Mvvm |
| DB | Microsoft.Data.Sqlite(+ 薄いリポジトリ層。ORMは使わない) |
| トレイアイコン | System.Windows.Forms.NotifyIcon(0.11A。.NET に標準で入っており、追加のライブラリが要らない。WinForms の部品はこれ以外に使わない。`UseWindowsForms` と、重なる名前の暗黙の using の除外はプロジェクトに設定済み) |
| 音声入出力 | NAudio、Concentus(Opusエンコード) |
| 文字起こし | Whisper.net(whisper.cpp)。モデルは初回起動時にユーザーの許可を得てダウンロードするか、同梱する |
| グラフ | ScottPlot.WPF または LiveCharts2 |
| ログ | Serilog(ファイル、ローリング) |
| ID | Ulid |
| テスト | xUnit |

## 7.2 モジュール構成

```
FocusCoach/
├─ src/
│  ├─ FocusCoach.Core/              net8.0(OS非依存)— ドメイン、状態機械、ガイド/指標/比較/再分類、イベント定義、パラメータ登録
│  ├─ FocusCoach.Storage/           net8.0 — SQLiteリポジトリ、JSONLのライター/リーダー
│  ├─ FocusCoach.Platform.Windows/  net8.0-windows — IWindowSystem / IActivitySource / IResourceSource の実装
│  ├─ FocusCoach.Audio/             net8.0-windows — IBgmPlayer / IRecorder / ITranscriber の実装
│  ├─ FocusCoach.App/               net8.0-windows(WPF)— 画面、ViewModel、トレイ、ホットキー
│  └─ FocusCoach.Cli/               net8.0 — scenario / replay / diag / dry-run コマンド(08章)。Windows専用のコマンドは実行時に判定する
├─ tests/FocusCoach.Core.Tests/     Fakeを使ったヘッドレステスト、シナリオファイル
└─ prototype/                       使い捨てのスパイク
```

### 依存の方向

`App → Core ← Platform/Audio/Storage`。Core はインターフェースだけを定義し、具体的な実装を知らない。

### スレッドモデル

- OS監視(フック、ポーリング)はバックグラウンドで動き、結果を**イベント**として `Channel<CoreEvent>` に投入する。
- Core はそのチャネルを**1件ずつ順番に(同時には走らない)**処理する(イベントループ)。そのため状態の更新に排他制御が要らず、イベントログの順序がそのまま処理順になる([08](08-debug-logging.md) のリプレイ可能性の前提)。
- UIスレッドは Core の状態スナップショットを購読して描画するだけで、Core の状態を直接変更しない。

## 7.3 実装フェーズ

| フェーズ | 内容 | 完了条件 |
|----------|------|----------|
| P-0 | スパイク: アクセス記録(U-05-2)、ウィンドウのスナップショット/最小化/復元、新規ウィンドウの検知、whisper の処理速度 | 各方式の可否と制約を `docs/spec` に反映する |
| P-1 | Core(種別、実行、サブタスク、指標)、Storage、CLIのシナリオ実行 | ヘッドレスで「開始→サブタスク→終了→指標」のテストが通る |
| P-2 | 最小UI: ホーム、開始準備、ミニパネル、タイマー、振り返り(数値のみ) | 実機で1タスクを最後まで記録できる |
| P-3 | ワークスペースの切替と復元(dry-runモードを先に作る) | 実機で元の画面に戻ることを確認する |
| P-4 | アクセス記録(Explorer、Recent、前面ウィンドウ、ブラウザ履歴)と資料ランチャー | 2回目の実行でランチャーから資料を開ける |
| P-5 | 振り返りの比較、改善ポイント、集中度タイムライン、履歴画面 | |
| P-6 | 作業メモ(テキスト/音声/文字起こし)、BGM、演出 | |
| P-7 | 再分類(統合/分割/付け替え/取り消し) | |
| P-8 | 中断からの復帰支援(割り込み退避、復帰カード、作業トレイ。09章の確定後) | |
| P-9 | 実利用と調整(しきい値、スコア式、演出) | 利用者が数週間使い、調整を反映する |

進捗と次の作業は `docs/plan.md` で管理する。

## 7.4 実装方針

- 仕様書(`docs/spec/`)を正とする。挙動を変えたら、同じ変更の中で該当する章と `docs/Version.md` を更新する。
- 技術的に不明な点は、`prototype/` の使い捨てスパイクで確認してから仕様に書く。
- 数値のしきい値(アイドル判定秒数、目標時間の係数、スコア式の係数など)は Core の**パラメータ登録**1か所に集約し、名前付きで実行時に変更できるようにする([08](08-debug-logging.md))。
- OS操作の中でウィンドウを閉じる処理は不可逆である。そのため `WM_CLOSE` を送る方法に限定し、強制終了は実装しない。

## 7.5 ローカル / クラウドセッションの作業分担

| 作業 | ローカル(Windows) | クラウドセッション(Linux) |
|------|:---:|:---:|
| 仕様書の編集・検討(09章など) | ○ | ○ |
| Core / Storage / Cli の実装と `dotnet test` | ○ | ○ |
| シナリオDSLでの挙動検証 | ○ | ○ |
| Platform.Windows / Audio のビルド確認 | ○ | △(`EnableWindowsTargeting` でコンパイルのみ可能) |
| WPF(App)のビルド確認 | ○ | △(Linux の SDK では WPF をビルドできない。`scripts/check-app-codebehind.sh` でコードビハインドの型チェックのみ) |
| 実機での起動、ウィンドウ操作、録音、アクセス記録の確認 | ○ | × |

- OSに依存するロジックは、極力 Core 側の純粋関数に寄せる(例: 「どのウィンドウを最小化対象にするか」の判定は Core が持ち、Platform は列挙と操作だけを行う)。そうすれば、クラウドセッションでもテストで検証できる範囲が広がる。
- クラウドセッションで Windows 固有の部分を変更した場合は、`docs/plan.md` に「要ローカル実機確認」として残す。

## 7.5a 多言語化(0.13A)

- 辞書: `src/FocusCoach.Core/Strings/ja.json`・`en.json`(キー → `string.Format` 形式の文)。Core の埋め込みリソース。`.resx` は使わない(自己完結の発行・クラウド(Linux)での型チェックと相性が悪く、辞書の追加が JSON のファイル追加で済む方が公開向き(言語を足す手順は CONTRIBUTING.md。切り替えの操作は日本語 ⇄ 英語の2つ固定なので、3言語目には選ぶ方法も要る))。
- `Core.Loc`: `T(key, args)`、`Language`、`SetLanguage`、`Resolve(setting, os)`、`Changed`、`Duration`・`Date`・`DateTime`(言語に合わせた書き方)。キー欠けは 言語 → en → `⟦キー⟧` で補い、実行は止めない。テスト用に `Loc.Use(lang)`(非同期の流れの中だけ言語を替える。他のテストに影響しない)。
- App: `{loc:T キー}`(`TExtension`)は `LocSource`(`Item[]` の変更通知)への束縛。言語が替わると `MainWindow.ApplyLanguage()` が、コードで設定している文言を作り直す。書体は `UiFont` を `DynamicResource` にして差し替える。
- テスト: 辞書の ja / en のキー・プレースホルダの一致、英語に日本語が混ざらないこと、**ソースに日本語の文字列リテラルが残っていないこと**の走査(許可は内部名・正規表現・既定文の3か所)。
- 新しい文言を足すときは、ja.json と en.json の両方にキーを足し、コードは `Loc.T` で引く(直書きするとテストが落ちる)。

## 7.6 実装状況(0.13A 時点)

経緯は `docs/Version.md`、実機での確認状況は `docs/plan.md`「要ローカル実機確認」を参照する。

| プロジェクト | 状態 |
|--------------|------|
| Core | 実装済み: 手順の一覧の変換・検証(StepList)、コンパクト表示の見た目と置き場所の判断(HudPolicy・HudPlacement)、Session(コマンド→イベント)、Reducer(畳み込み)、指標(F-11、式 v2)、ガイド/比較(F-02/F-11)、ワークスペース計画(F-03/F-10/09章 §9.4.2)、除外ルール、パラメータ登録、EventLoop、シナリオDSL、割り込み退避・再開メモ(どこまで / 次の一手)・最初の一手・復帰カード・縁の表示、離席/ロック/スリープの再分類、持ち越しと異常終了からの再開(09章) |
| Storage | 実装済み: JSONL ライター/リーダー(末尾の修復、書き込み中でも読める共有モード)、SQLite スキーマ v5(手順の登録 `subtask_templates`、開始準備の下書き `task_categories.draft_*` を含む)、タスク一覧(下書き・中止した実行を含む)・手順・下書きの読み書き、「一覧から外す」、再開候補の検出、アプリログ(スタックトレース付き)、設定ファイル。未実装: window_records / settings / exclusion_rules / category_change_log の各テーブル、再分類(F-13) |
| Cli | 実装済み: `scenario` / `replay` / `guide` / `workspace plan`(ドライラン)/ `diag`(OS に依存しない部分のみ) |
| Platform.Windows | **自宅PCで主な動作を確認済み**: ウィンドウの列挙・最小化・復元・WM_CLOSE、前面ウィンドウ/新規ウィンドウ/アイドルのポーリング、Explorer COM、ホットキー、WTS・電源の通知、画面共有からの除外、CoreHost。未実装: Recent、ブラウザ履歴、Office COM、WinEventHook |
| App | **自宅PCで主な動作を確認済み**: 開始準備(空から始まる、過去のタスクから選ぶプルダウン、手順エディタ S-02a)とミニパネルを1ウィンドウに同居させた画面、コンパクト表示(S-04b。別ウィンドウ、半透明・クリック透過・F8。元のウィンドウの左上を起点)、タスクトレイ(最小化の格納先、System.Windows.Forms.NotifyIcon)、アイコン、表示言語の切り替え(日本語 / 英語。0.13A)(デザインは 03章 §3.4.1)、メモ入力、ウィンドウ整理(S-06)、離席の問いかけ、復帰カード、閉じるときの確認、単一起動。未実装: トレイ、S-03 / S-05 / S-07 の画面、作業トレイの UI、BGM、演出、音声 |
| Audio | 未着手 |

- 実装中の暫定判断は、コード上に `[仮案-NN]` のマークを付けた。一覧は `docs/plan.md` にある。
- 新しい挙動は、`tests/scenarios/*.txt` のシナリオと xUnit のテストを先に(または同時に)追加する。
- Windows 実機でしか確かめられない部分は、`prototype/ui_render_probe`(画面を PNG にする)と UI Automation による操作で、利用者の手を借りずに確認できる範囲を広げている。

## 未決事項

- U-07-1: 技術スタックの確定(推奨案は .NET 8 + WPF)。
- U-07-2: 演出に WebView2 を使うかどうか(P-6 で WPF ネイティブの表現力を見て判断する)。
