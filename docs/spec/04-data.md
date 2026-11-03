# 04. データ仕様

> 状態: 初版(2026-10-03)

## 4.1 保存場所

`%LOCALAPPDATA%\FocusCoach\` をデータルートとする(設定で変更できる)。

| パス | 内容 | 正となる情報 |
|------|------|--------------|
| `focuscoach.db` | SQLite(WALモード)。種別、実行、サブタスク、アクセス記録、メモ、算出済み指標、設定 | エンティティの正 |
| `sessions/{run_id}.jsonl` | タスク実行ごとの生イベントログ(1行1イベント) | 生イベントの正。指標はここから再計算できる([08](08-debug-logging.md)) |
| `audio/{run_id}/{note_id}.opus` | 音声メモ | — |
| `logs/app-YYYYMMDD.log` | アプリログ | — |
| `models/` | 音声認識モデル(whisper) | — |

## 4.2 エンティティとテーブル

```
task_categories 1─* subtask_templates
       │1
       *
   task_runs 1─* subtask_runs ─* subtask_intervals
       │1  └───────────────┐
       ├─* pauses           │
       ├─* resource_accesses ┘(subtask_run_id で紐づけ。NULL可)
       ├─* notes
       ├─* window_records
       └─1 run_metrics

interrupt_categories 1─* interrupt_records 1─0..1 followups   (0.12A。タスクの実行をまたいで残る)
```

### task_categories(タスク種別)

| 列 | 型 | 説明 |
|----|----|------|
| id | INTEGER PK | |
| name | TEXT | 例: 開発計画書作成 |
| description | TEXT | |
| merged_into_id | INTEGER NULL | 統合された場合の統合先。統合元は非表示になるが削除しない(取り消しのため) |
| archived | INTEGER | 「一覧から外した」(プルダウンに出さない)。実行の記録は消さない。同じ名前を使うと 0 に戻る |
| draft_project | TEXT NULL | 開始準備の下書き: プロジェクト(スキーマ v5、2026-10-06) |
| draft_steps | TEXT NULL | 下書き: 手順(`名前:分` をカンマで区切った形式。例: `見積確認:25,執筆:40`) |
| draft_task_apps | TEXT NULL | 下書き: 作業で使うアプリ |
| draft_first_step | TEXT NULL | 下書き: 最初の一手 |
| last_used_at | TEXT NULL | 下書きを保存した日時。プルダウンの並びと「入力のみ」の判定に使う(NULL なら下書き無し) |
| created_at / updated_at | TEXT(ISO8601) | |

下書きは 1 タスクに 1 つで、保存のたびに上書きする(履歴は持たない)。実行の記録(`task_runs` など)とは別に扱う。

### subtask_templates(サブタスク定義)

| 列 | 型 | 説明 |
|----|----|------|
| id | INTEGER PK | |
| category_id | INTEGER FK | |
| seq | INTEGER | 既定の順序 |
| name | TEXT | |
| aliases | TEXT(JSON配列) | 統合時に吸収した別名。例: ["見積確認","見積書チェック"] |
| active | INTEGER | |
| target_min | REAL NULL | 目安(分)。手順エディタで登録する(05 F-01)。スキーマ v4(2026-10-04)で追加 |

手順エディタで「登録する」を選ぶと、そのタスクの行を丸ごと置き換える(削除して入れ直す)。

### task_runs(タスク実行)

| 列 | 型 | 説明 |
|----|----|------|
| id | TEXT PK | ULID(時系列ソートができ、JSONLのファイル名にも使う) |
| category_id | INTEGER FK | 再分類で付け替えられる |
| project_name | TEXT | 例: XXシステム更改 |
| target_sec | INTEGER NULL | 開始時に設定した目標時間 |
| started_at / ended_at | TEXT | |
| status | TEXT | `running` / `suspended`(後日再開のため中断中)/ `completed` / `aborted` / `interrupted`(クラッシュや強制終了で途切れ、未回復のもの)。**タスク開始時に `running` で行を作り**、状態が変わるたびに更新する(異常終了しても行が残るようにするため) |
| app_version | TEXT | |
| first_step | TEXT NULL | 開始時に書いた「最初の一手」(05 F-02)。次回の初期値に使う |

### subtask_runs(サブタスク実行)

| 列 | 型 | 説明 |
|----|----|------|
| id | INTEGER PK | |
| task_run_id | TEXT FK | |
| template_id | INTEGER NULL | その場で追加したサブタスクはNULL |
| name | TEXT | 実行時点の名前(テンプレートを後で改名しても実績の表記は変わらない) |
| seq | INTEGER | 実行した順序 |
| status | TEXT | `pending` / `active` / `done` / `skipped` |

サブタスクの集計値として `duration_sec`(区間の合計から一時停止を除いたもの)、`search_sec`(開始から最初の資料を開くまで。資料が無ければ NULL)、`target_sec`、`resume_note`(09章 §9.4.1 の「どこまでやったか」)、`next_step`(同「次の一手」)を持つ。履歴の比較を速くするため、指標と同じ式でタスク終了時に保存する。

同じサブタスクに戻ることがあるため、作業区間は子テーブル `subtask_intervals(subtask_run_id, started_at, ended_at)` に持つ。サブタスクの所要時間は、区間の合計から一時停止を除いたものとする。

### pauses(一時停止区間)

`task_run_id, started_at, ended_at, reason`。reasonは `interrupt` / `meeting` / `break` / `lock` / `sleep` / `remote` / `suspend` / `crash` / `other`、または自由記述。`away_reclassified` で割り込みに分類し直した区間は `interrupt` になる。

### resource_accesses(アクセス記録)

| 列 | 型 | 説明 |
|----|----|------|
| id | INTEGER PK | |
| task_run_id / subtask_run_id | | アクセス時点でアクティブだったサブタスク |
| kind | TEXT | `folder` / `file` / `url` |
| location | TEXT | 正規化したパス/URL(大文字小文字、末尾区切り、URLのフラグメントを正規化) |
| display_name | TEXT | ファイル名やページタイトル |
| source | TEXT | `explorer` / `recent` / `window_title` / `browser_history` / `office_com` / `manual` |
| first_seen_at / last_seen_at | TEXT | |
| launcher_pin | INTEGER | 次回の資料ランチャーに載せるか(振り返り画面で選ぶ。既定は自動判定) |

### notes(作業メモ)

`id, task_run_id, subtask_run_id, created_at, kind(text|voice), text, audio_path, duration_sec, transcription_status(none|pending|done|failed|external), transcription_engine`

### window_records(ワークスペースのウィンドウ記録)

`task_run_id, phase(snapshot|opened_during), hwnd, process_name, title, placement(JSON), z_order, monitor, close_result`。復元と、閉じる処理の結果を残すために使う。

### run_metrics(算出済み指標)

`task_run_id, formula_version, total_sec, paused_sec, idle_sec, active_sec, on_task_sec, off_task_sec, switch_count, switch_per_hour, focus_score, timeline(JSON: 5分刻みの集中率。アクティブ時間が無い刻みは null), interrupt_count, deferred_count, interrupt_paused_sec`。計算式を変えたら formula_version を上げ、JSONLから再計算する。2026-10-04: v2(サブタスクの時間からアイドルを除く、一時停止中・アイドル中の切り替えを数えない、短い実行の減点を按分する)。

### interrupt_categories / interrupt_records / followups(割り込みの出口とあとでやること。スキーマ v6、0.12A)

- `interrupt_categories`: `id, name(UNIQUE), archived, created_at, last_used_at`。出口カードのカテゴリ。初期データは「割込み作業」の1件だけ。使うと `last_used_at` を更新し、一覧から外したものも一覧に戻す。
- `interrupt_records`: `id, run_id, started_at, ended_at, category_id, title, outcome(done|followup), reentry_how(timeout|now|interrupt|closed), reentry_sec`。出口カードを閉じた直後に書く(タスクの終了を待たない)。
- `followups`: `id, created_at, source(interrupt|defer|manual), text, due_date(yyyy-MM-dd、NULL可), status(open|done|deleted), done_at, category_id, interrupt_id, title, task_name, windows_json, updated_at`。あとでやること。削除は論理削除。`windows_json` は片付けで最小化したウィンドウ(プロセス名・タイトル)。
- 移行は `CREATE TABLE IF NOT EXISTS` で、何度流しても壊れない。JSONL 側の記録はイベント `interrupt_closed` / `reentry_ended`(08章 §8.3)。

### category_change_log(再分類の履歴)

`id, at, op(merge|split|move_runs|rename|map_subtask), payload(JSON), undone_at`。再分類の取り消しに使う。

### settings / exclusion_rules

- `settings`: キーと値のペア。
- `exclusion_rules`: `kind(process|url_domain|path_prefix|title_regex), pattern, action(ignore|mask)`。記録しない対象の指定に使う。

## 4.3 セッションイベントログ(JSONL)

1行が1イベントで、`{"seq":12,"ts":"2026-10-03T10:15:02.123+09:00","type":"focus_changed","data":{...}}` の形式とする。type の一覧と用途は [08-debug-logging.md](08-debug-logging.md) §8.3 で定義する。

## 4.4 プライバシーと保持

- 外部へは送信しない(既定)。記録内容は振り返り画面から実行単位で削除できる。
- 除外ルールに一致した対象は、記録しない(ignore)か、タイトルやURLを伏せて種別のみ残す(mask)。既定では、プライベートブラウズ、パスワード管理アプリ、個人用メールなどを ignore とする。
- 保持期間: 既定は無期限。設定で「N日より古い生イベントログ(JSONL)を削除する」を選べる。集計済みの指標とアクセス記録は残す。
- タスク実行中以外は一切記録しない。記録はタスク開始から終了(一時停止中を除く)までに限る。
- 実データ(DB、JSONL、音声)はリポジトリにコミットしない。テスト用のデータは架空の内容で作る。

## 未決事項

- U-04-1: 生イベントログの既定保持期間(無期限か、180日か)。
- U-04-2: 他PCへの移行やバックアップ用のエクスポート/インポート形式。
