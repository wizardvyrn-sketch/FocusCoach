# prototype/

技術的な不明点を検証するための使い捨てスパイク置き場。製品のコードではない。

| スクリプト | 目的 | 状態 |
|-----------|------|------|
| `workpc_check.ps1` / `.cmd` | **業務PC用**の確認ツール。U-05-2 の各方式と、Office COM、無操作時間、Teams/Outlook の検出、配布の参考情報(.NET 8 ランタイム、AppLocker)を OK/NG/?? で表示する。結果をファイルに保存しない | 業務PCで実行待ち |
| `ui_close_flow_probe.ps1` | **自宅PC用**。隔離した保存先でアプリを起動し、UI Automation と Win32(疑似キー入力は使わない)で「集中を始める → × → 確認 → 振り返り」を再現して、ダイアログの文言と app ログを表示する。L-13 の不具合の再現と修正の確認に使った。`-Answer いいえ`、`-ReviewDelay 秒`、`-ClickDuringReview '終了'`(ボタン名は 0.6A の文言) | 使用済み(0.5C、0.6A)。回帰確認に再利用できる ※0.12A でダイアログを自前のもの(FcDialog)に替えたため、Win32 のメッセージボックスを探す部分はそのままでは動かない |
| `ui_render_probe/` | **自宅PC用**。アプリの各画面(開始準備、作業中の通常・目安超過・割り込みアプリ・中断中、復帰カード、離席の問いかけ、メモ、ウィンドウ整理)を、画面に出さずに PNG にする(WPF の RenderTargetBitmap)。`dotnet run --project prototype/ui_render_probe -- <出力フォルダ> [ja|en]`(言語を省略すると日本語。0.13A)。画面のキャプチャは、画面共有からの除外やロック中の画面で黒くなるので、見た目の確認はこれで行う | 使用済み(0.6A) |
| `icon_gen/` | アプリのアイコン(近未来・ペンで描く)を WPF の描画から作り、`app.ico`(16〜256px)と確認用 PNG を出す。`dotnet run --project prototype/icon_gen -- <出力フォルダ>`。絵を変えるときはここを直す | 使用済み(0.11A) |
| `access_capture_spike.ps1` | 管理者権限なしで、タスク中に開いたフォルダ・ファイル・URLを取得できるか(spec 05 F-06 / U-05-2)。Explorer COM、最近使った項目、前面ウィンドウ、ブラウザ履歴DBのコピー可否を確認する | 1回目を実行済み(2026-10-03)。最近使った項目が0件だったため、診断を追加した2回目を実行待ち |

`access_capture_spike.ps1` は、件数と種別(Local/UNC/Web)だけを表示し、実際のパスやURLは表示しない。ブラウザ履歴DBのコピー可否を確認するため、`History` ファイルをプロファイルごとに `%TEMP%\fc_spike_*.sqlite` へ一時コピーし、確認後にすぐ削除する(1回目の版はコピーを残していたので、`%TEMP%\fc_spike_Edge.sqlite` と `fc_spike_Chrome.sqlite` は手動で削除する)。

```powershell
powershell -ExecutionPolicy Bypass -File prototype\access_capture_spike.ps1
```

- リポジトリのルートで実行する。`prototype` フォルダの中にいる場合は `-File .\access_capture_spike.ps1` とする。
- `.\access_capture_spike.ps1` と直接実行すると、実行ポリシー(既定は Restricted)により「スクリプトの実行が無効」というエラーになる。上のコマンドは、その1回のプロセスだけポリシーを迂回する。システムの設定は変わらない。
- スクリプトは BOM 付き UTF-8 で保存している。Windows PowerShell 5.1 は BOM の無い UTF-8 を Shift-JIS として読み、日本語が文字化けしたり構文エラーになったりするため。

## workpc_check(業務PCでの確認)

業務PCは USB メモリへ書き込めないため、結果は画面にだけ出す。各行は `[OK] C1 ...` の形式で、最後に `SUMMARY C0=OK C1=OK ...` の1行を出す。件数と種別(Local/UNC/Web)だけを表示し、ファイル名・パス・URL・ウィンドウタイトルは出さない。

| ID | 確認内容 | 関連 |
|----|---------|------|
| C0 | PowerShell の言語モード(FullLanguage でないと COM や Win32 が使えない)、グループポリシーの実行ポリシー | 全体 |
| C1 | Explorer で開いているフォルダのパス | F-06 |
| C2 | 最近使った項目の記録と、無効化の設定・ポリシー | F-06、U-05-2 |
| C3 | 前面ウィンドウのプロセス名とタイトル(5秒以内に Excel 等をクリック) | F-06、F-11 |
| C4 | 無操作時間(GetLastInputInfo) | F-11、F-15 |
| C5 | Excel/Word/PowerPoint で開いている文書のフルパス(COM) | F-06(Phase 2 方式) |
| C6 | Edge/Chrome の履歴DBを全プロファイルで一時コピーできるか(すぐ削除) | F-06 |
| C7 | Teams/Outlook をプロセス名で検出できるか | 09章 §9.6 |
| C8 | .NET 8 デスクトップランタイムの有無、AppLocker の規則数(参考) | U-07-1 |

実行方法(上から順に試す):

1. `workpc_check.cmd` をダブルクリックする(`workpc_check.ps1` と同じフォルダに置く)。
2. PowerShell で `powershell -NoProfile -ExecutionPolicy Bypass -File .\workpc_check.ps1`。
3. グループポリシーでスクリプトファイルの実行が止められている場合は、`workpc_check.ps1` の中身をすべてコピーし、PowerShell の画面に貼り付けて Enter を押す。
