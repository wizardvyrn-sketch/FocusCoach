# 08. デバッグ性を考慮した設計、ログ設計方針

> 状態: 初版(2026-10-03)

## 8.1 基本方針

1. **Core はイベントの畳み込み(fold)として作る**: Core の状態は「初期状態 + イベント列」だけで決まる。時刻もイベントとして外から与える(`IClock` を注入する)。こうすることで、記録したイベントログを入力すれば、どの実行も**UIもOSも無しに再現できる**。
2. **OS依存はインターフェースの向こう側に置く**: `IWindowSystem`、`IActivitySource`、`IResourceSource`、`IBgmPlayer`、`IRecorder`、`ITranscriber`。テストとシナリオ実行では Fake 実装を使う。
3. **入力経路を1本にする**: UIのボタン、ホットキー、CLIのシナリオは、いずれも同じ `CoreCommand` を Core に送る。UIだけにある処理経路を作らない。
4. **不可逆なOS操作にはドライランを用意する**: ワークスペースの最小化、復元、ウィンドウを閉じる処理は、「何をするか」だけをログに出すモードで確認できるようにする。

この方針により、Windows実機が無いクラウドセッションでも、Core の挙動のほとんどをテストで検証できる([07](07-tech-stack.md) §7.5)。

## 8.2 ヘッドレス検証

### シナリオDSL(CLI: `fc scenario <file>`)

```
# tests/scenarios/basic_two_subtasks.txt
at 00:00 start_task category="開発計画書作成" project="XX"
at 00:00 focus process=EXCEL.EXE title="見積書.xlsx - Excel" task_window=yes
at 00:10 resource kind=file location="\\fs01\見積\XX\見積書.xlsx" source=recent
at 00:12 next_subtask
at 00:15 focus process=OUTLOOK.EXE title="受信トレイ" task_window=no
at 00:18 focus process=WINWORD.EXE title="概略仕様書.docx - Word" task_window=yes
at 00:20 idle_start            # 最後の入力が 00:20
at 00:24 idle_end              # 4分 ≥ idle_threshold_sec(120秒)なので、区間全体をアイドルとする
at 00:40 end_task
expect subtask[1].duration == 12m
expect metrics.idle_sec == 240
expect metrics.switch_count == 1
expect resources.count == 1
```

- 時刻は `HH:MM[:SS]`(開始からの経過。`00:12` = 12分)。サブタスクの番号は 1 始まり(`subtask[1]` = 最初のサブタスク)。
- `at <時刻> !<コマンド>` は、そのコマンドが**拒否される**ことを期待する(拒否されなければ失敗)。
- `at <時刻> expect ...` とすると、その時刻の状態で評価する(目安超過や割り込みアプリ滞在の判定に使う)。
- expect で使えるパス: `subtask[n].duration|name|status|target|search|resume_note`、`metrics.<名前>`、`params.<名前>`、`resources.count`、`interrupts.count`、`deferred.count`、`tray.count`、`notes.count`、`opened_during.count`、`events.count`、`status`、`paused`、`edge`、`foreground.task_window`、`resume_card.subtask|note|resources|paused_sec`。演算子は `== != >= <= > < ~=`(`~=` は ±0.5)。値は `12m` `1h30m` `45s` のように単位を付けられる。
- `at` は仮想時刻であり、実時間は待たない。`expect` に失敗するとCLIは非ゼロで終了する。そのため xUnit からそのまま呼べる。
- `set <param>=<value>` でパラメータを変更できる(§8.6)。範囲外や未登録の名前は拒否される。
- Core のテストは、このシナリオファイル群 + xUnit を主体とする。

### リプレイ(CLI: `fc replay sessions/{run_id}.jsonl`)

- 実機で記録したセッションのJSONLを Core に再投入し、指標を再計算して表示する。集中スコア式を変えたときの比較(`--formula v2`)や、バグ報告の再現に使う。

### 診断(CLI: `fc diag`)

- 次を一括でテキスト出力する。OSのビルド、.NETのバージョン、モニタ構成とDPI、Explorer COMが使えるか、Recent機能が有効か、検出したブラウザとプロファイル、ブラウザ履歴DBをコピーできるか、マイクデバイス、データルートの容量、DBのスキーマバージョン。
- 不具合の問い合わせ時に、まずこの出力を見る。

### ワークスペースのドライラン(CLI: `fc workspace --dry-run snapshot|minimize|restore`)

- 実際にはウィンドウを動かさず、対象として選ばれるウィンドウと、各ウィンドウに行う予定の操作を一覧で出す。除外判定(ツールウィンドウ、所有ウィンドウ、クローク状態、UWPの枠)のバグを見つけるために使う。
- 判定ロジック自体は Core にあるため、列挙結果を JSON で保存しておけば、クラウドセッションでもテストで再現できる。

## 8.3 ログの種類

| ログ | 場所 | 形式 | 目的 | 保持 |
|------|------|------|------|------|
| セッションイベントログ | `sessions/{run_id}.jsonl` | JSON Lines | 実行の完全な再現(リプレイ)。指標の再計算 | 設定による(U-04-1) |
| アプリログ | `logs/app-YYYYMMDD.log` | テキスト(Serilog、構造化) | 動作の追跡、エラー調査 | 14日でローテーション |
| クラッシュログ | `logs/crash-{timestamp}.txt` | テキスト | 未処理例外のスタックと直前の状態 | 30日 |

### セッションイベント type 一覧(初版)

| 分類 | type | 主なdata |
|------|------|----------|
| ライフサイクル | `session_start` | app_version、os_build、設定のハッシュ、パラメータ一覧 |
| | `task_started` / `task_ended` / `task_aborted` | category_id、project、target_sec、first_step |
| 進行 | `subtask_started` / `subtask_completed` / `subtask_skipped` / `subtask_added` | subtask_run_id、name |
| | `paused` / `resumed` | reason(`interrupt` / `lock` / `sleep` / `remote` / `suspend` / `crash` など) |
| | `task_suspended` / `session_resumed` | 持ち越しの中断と再開。`session_resumed` の reason は `next_day` / `crash`、gap_sec |
| | `away_reclassified` | from、to(区間)、as=`interrupt`、元の分類(`idle` / `lock` / `sleep` / `remote`) |
| 活動 | `focus_changed` | process、title(マスク後)、hwnd、task_window判定 |
| | `idle_started` / `idle_ended` | last_input_at |
| | `window_opened` / `window_closed` | hwnd、process、title |
| 資料 | `resource_seen` | kind、location、source |
| ワークスペース | `ws_snapshot` / `ws_minimize` / `ws_restore` / `ws_close` | `ws_snapshot` は phase(`start` / `interrupt`)、boot_id(Windows の起動ごとの識別子)、ウィンドウの一覧(hwnd、プロセス名、placement)。操作系は対象ウィンドウごとの結果(成功/失敗/タイムアウト)。異常終了からの復帰で、ウィンドウを元に戻すために使う(F-16) |
| メモ | `note_added` / `recording_started` / `recording_stopped` / `transcription_done` | note_id |
| 中断・復帰 | `interrupt_marked` / `interrupt_note_added`(index、text=どこまで、next=次の一手)/ `interrupt_deferred` / `resume_card_shown` / `workspace_restored` / `tray_item_added` | 種類、resume_note の有無、戻したウィンドウ数など([09](09-focus-advice.md) §9.7) |
| 調整 | `param_set` | name、old、new(実行時のパラメータ変更も記録し、リプレイの結果を一致させる) |

- コマンドの data に `origin=monitor` が付いた入力(OS監視由来)は、時刻が直前のイベントより前でも、直前の時刻に切り上げて受け付ける。
- `seq` は単調に増やす。再開(`session_resumed`)後も、同じファイルで続きの番号から増やす。書き込みは1イベントごとに追記し、Flushする。クラッシュしても直前までが残るようにするためである。
- 指標は `formula_version` とともにDBへ保存する。JSONLが残っていれば、いつでも再計算できる。

### アプリログのカテゴリとレベル

- カテゴリ: `Core`、`Workspace`、`Monitor.Foreground`、`Monitor.Explorer`、`Monitor.Recent`、`Monitor.Browser`、`Audio`、`Transcribe`、`Storage`、`UI`。
- レベル: 既定は Information。設定画面またはCLIからカテゴリごとに Debug へ上げられる。
- **Workspaceカテゴリは既定で詳細に出す**。ウィンドウ操作が「戻らない」「閉じた/閉じない」という不具合は再現が難しいため、各操作の前後の状態(placement、可視性)を必ず記録する。

## 8.4 プライバシーとログ

- アプリログには、既定でウィンドウタイトル、パス、URLを**マスク**して出す(種別とハッシュの先頭8桁のみ)。詳細ログモード(期限付きで、24時間後に自動的にOFFになる)の間だけ生の値を出す。
- セッションイベントログはローカル専用のデータとして生の値を持つ。ただし除外ルール([04](04-data.md) §4.4)は記録の時点で適用する。
- `fc diag` の出力には個人データを含めない。
- 実機のログやJSONLをリポジトリにコミットしない(`.gitignore` で除外する)。テストに使う場合は、内容を架空のものに置き換えた上で `tests/` に置く。

## 8.5 アプリ内デバッグパネル

- 隠しホットキー(既定: Ctrl+Alt+Shift+D)で開く。
- 表示: 直近のイベントのストリーム、現在の状態(アクティブなサブタスク、オンタスク判定、アイドル状態)、リアルタイムの指標、パラメータ一覧。パラメータはその場で変更でき、変更は `param_set` として記録する。
- 09章のアドバイス判定を調整するとき、シグナルの値と発火条件をここで確認する。

## 8.6 パラメータ登録

- すべてのしきい値と係数を `Core.Params` の1か所に、名前、既定値、範囲、説明とともに登録する。例: `idle_threshold_sec=120`、`away_prompt_sec=300`、`score_full_weight_sec=600`、`guide_history_n=5`、`target_ratio=0.95`、`focus_switch_penalty=2`、`foreground_poll_ms=2000`。
- 設定画面、デバッグパネル、シナリオDSL(`set name=value`)のいずれからも同じ名前で変更できる。

## 8.7 障害時の扱い

- 未処理例外が起きたら、クラッシュログを書き、実行中のタスクを `interrupted` にしてから終了する。次回の起動時に復帰ダイアログを出す(F-16)。
- OS監視の失敗(COMの例外、履歴DBのロックなど)はその監視源だけを無効にして続行する。その旨を Warning ログに出し、振り返り画面に「今回はURL記録が取得できませんでした」と表示する。記録が一部欠けても、タスクの進行は止めない。

## 未決事項

- U-08-1: 利用者がバグ報告を出すときの手順(diagの出力と該当するJSONLをまとめるコマンドを用意するか)。

## 8.x 表示言語とログ(0.13A)

- ログ(アプリログ・JSONL・クラッシュログ)・例外のメッセージ・シナリオのコマンドは、表示言語に依存させない(英語)。画面に出す文だけを辞書(03章 §3.7)から引く。
- 言語を切り替えたら、アプリログに `UI: language changed ja->en` を1行出す。
