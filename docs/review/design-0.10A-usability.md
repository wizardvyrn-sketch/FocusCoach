# 0.10A 使い勝手の改善 — 対策設計とコード修正案

> 状態: **実装済み(2026-10-06、0.10A)。** 利用者の回答(Q1〜Q5)を反映した。実装で設計から変えた点は §7 に記録する。
> 入力: 利用者が 0.9A を使って見つけた4点(下の §0)。関係する仕様: 03 S-02 / S-04、04 task_categories、05 F-01・F-02、09 §9.4.1・§9.4.6。

## 0. 要望と解釈

| # | 利用者の要望 | 解釈(設計の前提) |
|---|-------------|------------------|
| R1 | タスクは既定で空から始めたい。前回と同じタスクをまた始めることは少ない | 起動時とタスク終了後の開始準備を、タスク名・プロジェクト・手順・最初の一手が**空**の状態にする。過去のタスクはプルダウンから選べば足りる |
| R2 | タスクが保存されず、一度きりの入力が次の起動で消える。閉じるときにファイルなどに記録してほしい | 開始準備で入力した内容(タスク名・プロジェクト・手順・使うアプリ・最初の一手)を、**閉じるとき・開始するとき・手順を編集したとき**に保存する。終了しなかったタスク(入力だけ、中止、中断中)もプルダウンに出す |
| R3 | 閉じるボタンを適切な場所に置いてほしい | タイトル帯の ✕ が小さく薄い(10pt・灰色)うえ、作業中パネルの「終了」(タスクを終える)と区別しにくい。**Windows 標準と同じ右上の並び・大きさ**にし、タスクの「終了」とは文言で分ける(§3 の解釈は利用者に確認する) |
| R4 | 必要な要素だけに縮める・戻すボタンと、ファンクションキーがほしい。縮めたら背景を透過して、画面の操作・視認をできるだけ妨げない | 「コンパクト表示」を追加する: 1行の小さな表示(状態・サブタスク名・経過時間・縁の色)。半透明で、**クリックは下のアプリに通す**(クリック透過)。F8 で切り替え |

## 1. 現状の原因(0.9A のコード)

| # | 原因 | 場所 |
|---|------|------|
| R1 | 起動時に最近使ったタスクを選ぶ。XAML の初期値が「開発計画書作成」、手順の初期値が例の3手順 | `MainWindow.xaml.cs` `RefreshCategories(selectLatest: true)`、`MainWindow.xaml` `CategoryBox Text="開発計画書作成"`、`_steps = StepList.Parse("見積確認:25,…")` |
| R2 | プルダウンに出すのは「終了した実行があるタスク」か「手順を登録したタスク」だけ。開始準備の入力はどこにも保存していない(閉じるときに保存するのはウィンドウ位置などの設定だけ) | `FocusCoachDb.ListCategories()` の条件、`Window_Closing` |
| R3 | タイトル帯のボタンは `CaptionButton`(FontSize 10、Padding 10,2、文字色 TextMuted)。ホバーしても色がほとんど変わらない | `App.xaml` `CaptionButton`、`MainWindow.xaml` タイトル帯 |
| R4 | コンパクト表示が未実装(03 S-04 に「折りたたむと1行表示」とあるだけ)。WPF の透過は `AllowsTransparency=True` が必要で、**ウィンドウを表示した後には切り替えられない** | — |

## 2. 対策設計

### R1 既定で空から始める

**振る舞い**
- 起動時と、タスクを終えて開始準備に戻ったときは、タスク名・プロジェクト・手順・最初の一手を空にする(作業で使うアプリと練習モードは設定の値を保つ)。
- タスク欄が空の間は、薄い文字で「タスクを選ぶか、名前を入力」と出す(プレースホルダー)。
- タスク名が空の間は [集中を始める] を押せない(ボタンを無効にし、下に「タスクを選ぶと始められます」)。現状は空でも始められ、名前が空の種別ができてしまう。
- ガイド欄は「タスクを選ぶと、過去の目安が出ます」。
- タスクを選ぶ(または名前を入れる)と、今と同じように手順などが入る(R2 の下書きを含む)。

**処理構造**
- `ResetStartPanel()` を新設し、コンストラクタと `EnterStartMode()` から呼ぶ。中身: `CategoryBox.Text = ""`、`ProjectBox.Text = ""`、`FirstStepBox.Text = ""`、`_steps = []`、`_stepsCategory = null`、`ShowSteps("")`、`UpdateStartEnabled()`。
- `RefreshCategories(selectLatest: false)` に統一し、`selectLatest` 引数は削除する。
- `UpdateStartEnabled()`: `StartButton.IsEnabled = CategoryBox.Text.Trim().Length > 0`。`OnCategoryChanged()` の最後で呼ぶ。
- 再開候補(中断中・異常終了)の表示は今のまま(タスク欄が空でも出る)。

### R2 入力したタスクを保存する(下書き)

**振る舞い**
- 開始準備の入力を、タスクごとの**下書き**として DB に保存する。保存するのは、タスク名・プロジェクト・手順・作業で使うアプリ・最初の一手・保存日時。
- 保存するとき: (a) [集中を始める] を押したとき、(b) 開始準備の状態でアプリを閉じるとき(タスク名が空なら保存しない)、(c) 手順エディタで [決定] したとき(「登録する」を選ばなくても、下書きとしては残す)。
- プルダウンに出す条件を広げる: 終了した実行がある、手順を登録した、**下書きがある、実行が1回でもある(中止・中断中も含む)**。並びは「最後に使った日時」(終了した日時と下書きの保存日時の新しい方)の新しい順。
- タスクを選んだときに入れる内容(優先順):
  - 手順: 登録した手順 → 下書きの手順 → 最後に終了した実行の手順
  - プロジェクト: 下書き → 空
  - 作業で使うアプリ: 下書き → 設定の既定値
  - 最初の一手: 下書き → 前回の実行の最初の一手(今の `LoadLastFirstStep`)
- 手順の下の表示は、「登録した手順」「前回入力した手順(下書き)」「前回の実行の手順」で出どころを分ける。
- 入力の誤りでできた不要なタスクが一覧に増えるので、プルダウンの各行に「一覧から外す」(アーカイブ)を付ける(§5 の Q3 で要否を確認。最小案は右クリックメニュー)。

**データ(スキーマ v5)**

`task_categories` に列を足す(1タスク1下書き。実行の記録 `task_runs` とは分ける)。

| 列 | 型 | 説明 |
|----|----|------|
| draft_project | TEXT NULL | 下書きのプロジェクト |
| draft_steps | TEXT NULL | 下書きの手順(`StepList.Format` の形式。例: `見積確認:25,執筆:40`) |
| draft_task_apps | TEXT NULL | 下書きの作業で使うアプリ |
| draft_first_step | TEXT NULL | 下書きの最初の一手 |
| last_used_at | TEXT NULL | 下書きを保存した日時(開始・閉じる・手順の決定) |

別表にしない理由: 1タスクに1つで十分、`ListCategories` の1回の問い合わせで並べ替えまでできる、既存の `EnsureCategory` をそのまま使える。

**処理構造**
- Storage: `public sealed record TaskDraft(string Category, string Project, List<StepItem> Steps, string TaskApps, string FirstStep, DateTimeOffset SavedAt);`
  - `void SaveDraft(TaskDraft d)` — `EnsureCategory` のあと UPDATE。名前の前後の空白は取る。
  - `TaskDraft? LoadDraft(string category)`
  - `ListCategories()` — 条件と並びを上のとおりに変更。`CategoryInfo` に `DateTimeOffset? LastUsed` を足す(表示の「最終」は `LastUsed`)。
  - `Archive(string category)` — `archived=1`(Q3 で採用する場合)。
- CoreHost: `SaveDraft` / `LoadDraft` の薄いラッパー(DB の失敗はログに出して握りつぶす。作業を止めない)。`LoadSteps` は優先順に下書きを加える。
- MainWindow:
  - `CollectDraft()` で画面から `TaskDraft` を作る。`Start_Click` の最初、`EditSteps_Click` の決定後、`Window_Closing` の中(`Host.IsRunning` でないとき)で `Host.SaveDraft(...)`。
  - `LoadStepsFor(category)` を `ApplyTaskFor(category)` に広げ、プロジェクト・アプリ・最初の一手も入れる。ただし利用者が書き換えた欄は上書きしない(今の `PrefillFirstStep` と同じ「自動で入れた値と同じときだけ上書き」の方式を各欄に使う)。
- 実行中に閉じる場合は今のまま(終了 → 保存、中断 → 持ち越し)。異常終了したときの開始準備の入力は保存されない(閉じる処理を通らないため)。必要なら、入力の変化から2秒後に保存する案がある(§5 の Q4)。

### R3 閉じるボタンの配置

**振る舞い(提案)**
- タイトル帯の右端を、Windows 標準の並びにする: `[▭ コンパクト] [– 最小化] [✕ 閉じる]`。各ボタンは 46×34px(標準のキャプションボタンと同じ大きさ)、アイコン 12pt、文字色は Text。
- ✕ だけ、ホバー時の背景を落ち着いた赤み(例: `#7A3E48`)にして「閉じる」だと分かるようにする(強い赤は使わない。03 §3.4.1)。ツールチップ「アプリを閉じる」。
- 作業中パネルの「終了」は「タスク完了」に改め、アプリを閉じる ✕ と取り違えないようにする(「あとで続ける」はそのまま)。
- コンパクト表示には ✕ を置かない(誤って閉じないように)。

**処理構造**
- `App.xaml`: `CaptionButton` を Width 46・Height 34・FontSize 12・Foreground Text に変更。`CloseCaptionButton`(BasedOn CaptionButton、IsMouseOver で背景 `#7A3E48`・文字 白)を追加。
- `MainWindow.xaml`: タイトル帯のボタンを3つにし、✕ に `CloseCaptionButton` を当てる。作業中の「終了」ボタンの文言を変更(UI Automation の確認ツールのボタン名も合わせる)。

### R4 コンパクト表示(縮める / 戻す)

**振る舞い**
- 切り替え: タイトル帯の [▭] ボタン、または **F8**(作業中・開始準備のどちらでも)。もう一度 F8 で元に戻す。キーは設定で変えられる(`HotkeyCompact`)。
- コンパクト表示の中身(1行、幅 約 260px・高さ 約 34px):
  `● 概略仕様確認 0:18:24` と、縁の状態(目安超過は琥珀、割り込みアプリは青、中断中は灰青)を左端の細い色帯とアイコンで出す。開始準備のときは `● FOCUS COACH 待機中`。
- **透過**: 背景は半透明(既定 55%)、文字は不透明で薄い影を付けて、下の画面が見える状態でも読めるようにする。
- **クリック透過**: コンパクト表示の上をクリックしても、下のアプリに届く(表示がクリックを受けない)。マウスを重ねると、さらに薄くなる(既定 20%)ので、下の画面の文字も見える。
- **触りたいとき**: Ctrl を押しながらマウスを重ねている間だけ、クリックを受け付けて不透明になる。そのとき [⤢ 戻す] ボタンが使え、ドラッグで移動もできる。
- **安全策**: F8 を登録できなかったとき(他のアプリが使用中)は、クリック透過にしない(戻すボタンを普通に押せる)。
- **一時的に戻す**: コンパクト表示の間に、離席の問いかけ・復帰カード・閉じる確認・振り返りなど利用者に見せる必要があるものが出たら、自動で元の表示に戻す。それらが閉じたら(自動で畳まれた場合も)、コンパクト表示に戻る。F9 / F10 はコンパクト表示のまま使える。
- 位置: コンパクト表示は、元のパネルの**右上の角**に合わせて出す。コンパクト表示を動かしたら、戻したときのパネルもその角に合わせる。
- 画面共有: コンパクト表示も画面共有から除外する(09 §9.4.6)。

**処理構造(別ウィンドウ方式)**

透過(`AllowsTransparency=True`)は表示後に切り替えられないため、**コンパクト表示を別のウィンドウ `CompactHud` にする**。メインのウィンドウは手を入れず、コンパクト表示の間は隠す(`Hide()`)。

```
                F8 / [▭]                         F8 / [⤢] / Ctrl+クリック
   ┌──────────┐ ───────────▶ ┌──────────────┐ ───────────────▶ ┌──────────┐
   │ Full     │              │ Compact      │                  │ Full     │
   │ Main 表示 │ ◀─────────── │ Main 隠す     │                  └──────────┘
   └──────────┘   自動で戻る   │ Hud 表示      │
        ▲        (問いかけ等)  └──────────────┘
        │                          │ 問いかけ・復帰カード・確認・振り返りが出る
        │                          ▼
        │                  ┌──────────────────┐  それらが閉じた
        └──────────────────│ FullTemporary     │ ─────────────▶ Compact
                           │ Main 表示(一時)   │
                           └──────────────────┘
```

- 状態: `enum ViewMode { Full, Compact }` と `bool _tempFull`(一時的に戻している)。`ToggleCompact()`、`ShowFullTemporarily()`、`EndTemporaryFull()`。
- メインのウィンドウを隠しても、ホットキー(WM_HOTKEY)とロック・スリープの通知は同じウィンドウハンドルに届き続ける(ハンドルは破棄されない)。
- `CompactHud`(新規、コードで作る Window):
  - `WindowStyle=None`、`AllowsTransparency=True`、`Background=Transparent`、`Topmost`、`ShowInTaskbar=False`、`ShowActivated=False`、`SizeToContent=WidthAndHeight`。
  - 表示時に拡張スタイル `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`、クリック透過のときは `WS_EX_TRANSPARENT` も足す(`WS_EX_LAYERED` は AllowsTransparency で付く)。`ExcludeFromCapture` を掛ける。
  - `Update(StateSnapshot s)` / `ShowIdle()` で中身を更新する。メインの `Render` から呼ぶ。
  - 150ms ごとのタイマーで、カーソルの位置(`GetCursorPos`)と Ctrl の状態(`GetKeyState(VK_CONTROL)`)を見て、不透明度とクリック透過を切り替える。判定は Core の純粋関数にする(下)。
- Core(新規 `HudPolicy`。OS に依存しないのでクラウドでもテストできる):
  ```csharp
  public sealed record HudLook(bool ClickThrough, double Opacity);
  public static class HudPolicy
  {
      // compactOpacity: 通常時の不透明度(既定 0.55)、hoverOpacity: マウスを重ねたとき(既定 0.2)
      public static HudLook Decide(bool hotkeyOk, bool clickThroughSetting, bool cursorOver, bool ctrlDown,
                                   double compactOpacity, double hoverOpacity)
      {
          var canClickThrough = hotkeyOk && clickThroughSetting;    // F8 が使えないときは透過しない(戻れなくなるため)
          if (!canClickThrough) return new(false, cursorOver ? 1.0 : compactOpacity);
          if (cursorOver && ctrlDown) return new(false, 1.0);        // Ctrl を押して触る
          return new(true, cursorOver ? hoverOpacity : compactOpacity);
      }
  }
  ```
- 設定(`AppSettings`、settings.json): `HotkeyCompact = 0x77`(F8)、`CompactOpacity = 0.55`、`CompactHoverOpacity = 0.2`、`CompactClickThrough = true`、`CompactRight` / `CompactTop`(位置。右上の角)。起動時はいつも通常表示から始める(コンパクトのまま起動すると戻し方に迷うため)。
- NativeMethods: `WS_EX_TRANSPARENT = 0x20`、`WS_EX_LAYERED = 0x80000`、`GetCursorPos`、`GetKeyState`、`VK_CONTROL = 0x11`。
- アプリログ: 切り替えを Info で出す(`UI: view compact` / `view full` / `view full (temporary: resume_card)`)。セッションの JSONL には記録しない(画面の都合で、作業の記録ではないため)。

**F8 を選んだ理由と影響**
- F9 / F10 と並びで押しやすい。利用者の常用アプリ(Word / Excel / Outlook / Teams / ブラウザ)では、F8 は「選択範囲の拡張」程度で使用頻度が低い。F11(全画面)・F12(開発者ツール・名前を付けて保存)は避ける(09 §9.4.1)。
- グローバルホットキーなので、FocusCoach の起動中は F8 が他のアプリに届かない。不都合なら設定で変える(Q1)。

## 3. 変更するファイルとコード修正案

| ファイル | 変更 |
|----------|------|
| `src/FocusCoach.Core/HudPolicy.cs`(新規) | §2 R4 の `HudLook` / `HudPolicy.Decide` |
| `src/FocusCoach.Storage/Database.cs` | スキーマ v5(`task_categories` に draft_* と last_used_at)。`TaskDraft`、`SaveDraft`、`LoadDraft`、`ListCategories` の条件と並び、`CategoryInfo.LastUsed`、(Q3 次第で)`Archive` |
| `src/FocusCoach.Storage/AppLog.cs`(AppSettings) | `HotkeyCompact`、`CompactOpacity`、`CompactHoverOpacity`、`CompactClickThrough`、`CompactRight`、`CompactTop` |
| `src/FocusCoach.Platform.Windows/NativeMethods.cs` | `WS_EX_TRANSPARENT`、`WS_EX_LAYERED`、`GetCursorPos`、`GetKeyState`、`VK_CONTROL` |
| `src/FocusCoach.Platform.Windows/CoreHost.cs` | `SaveDraft` / `LoadDraft`、`LoadSteps` の優先順に下書きを追加 |
| `src/FocusCoach.App/CompactHud.cs`(新規) | コンパクト表示のウィンドウ。`Update` / `ShowIdle` / 透過とクリック透過の切り替え / Ctrl+ドラッグで移動 |
| `src/FocusCoach.App/MainWindow.xaml` | タスク欄のプレースホルダー、タイトル帯のボタン3つ(▭ – ✕)、作業中の「終了」→「タスク完了」 |
| `src/FocusCoach.App/MainWindow.xaml.cs` | `ResetStartPanel`、`UpdateStartEnabled`、`CollectDraft` と保存の呼び出し、`ApplyTaskFor`、F8 の登録と WM_HOTKEY(ID 3)、`ToggleCompact` / `ShowFullTemporarily` / `EndTemporaryFull`、`Render` から HUD を更新 |
| `src/FocusCoach.App/App.xaml` | `CaptionButton` の大きさ、`CloseCaptionButton`、プレースホルダー用の文字色 |
| `tests/FocusCoach.Core.Tests/` | `DraftTests`(保存・読み込み・一覧の条件と並び・v4→v5 の移行)、`HudPolicyTests`、`RecoveryTests.Db_migrates_from_v1_to_latest` に v5 の列の削除を追加 |
| `prototype/ui_render_probe` / `ui_close_flow_probe.ps1` | 空の開始準備・コンパクト表示の描画を追加。ボタン名の変更(「タスク完了」)に追従 |
| 仕様 03 / 04 / 05 / 09、Version、plan | 下の §4 |

### 主なコード修正案(抜粋)

**MainWindow.xaml.cs — 開始準備のリセットと下書き**
```csharp
private void ResetStartPanel()
{
    CategoryBox.Text = ""; ProjectBox.Text = ""; FirstStepBox.Text = "";
    _steps = []; _stepsCategory = null; _auto = new();   // 自動で入れた値の控え(利用者の書き換えを守る)
    ShowSteps("");
    UpdateStartEnabled();
}

private TaskDraft CollectDraft() => new(CategoryBox.Text.Trim(), ProjectBox.Text.Trim(), _steps,
    TaskAppsBox.Text.Trim(), FirstStepBox.Text.Trim(), DateTimeOffset.Now);

private void SaveDraftIfAny()
{
    if (CategoryBox.Text.Trim().Length == 0) return;
    Host.SaveDraft(CollectDraft());               // 失敗はログに出すだけ
}
// Start_Click の最初、EditSteps_Click の決定後、Window_Closing(!Host.IsRunning のとき)で SaveDraftIfAny()
```

**MainWindow.xaml.cs — タスクを選んだときの自動入力**
```csharp
private void ApplyTaskFor(string category)
{
    if (category == _stepsCategory) return;
    _stepsCategory = category;
    var draft = Host.LoadDraft(category);
    var steps = Host.LoadSteps(category);          // 登録した手順 → 下書き → 前回の実行 の順(CoreHost 側)
    if (steps is { } s) { _steps = s.Steps; ShowSteps(s.SourceLabel); } else ShowSteps(category.Length == 0 ? "" : "新しいタスク");
    FillIfUntouched(ProjectBox, ref _auto.Project, draft?.Project ?? "");
    FillIfUntouched(TaskAppsBox, ref _auto.Apps, draft?.TaskApps ?? Host.Settings.DefaultTaskApps);
    FillIfUntouched(FirstStepBox, ref _auto.FirstStep, draft?.FirstStep ?? Host.LoadLastFirstStep(category) ?? "");
}

/// 欄が空か、前回自動で入れた値のままなら、新しい値で上書きする(利用者が書いた内容は残す)
private static void FillIfUntouched(TextBox box, ref string auto, string value)
{
    if (box.Text.Length > 0 && box.Text != auto) return;
    auto = value; box.Text = value;
}
```

**MainWindow.xaml.cs — コンパクト表示の切り替え**
```csharp
private const int HotkeyCompactId = 3;
private enum ViewMode { Full, Compact }
private ViewMode _view = ViewMode.Full;
private bool _tempFull;
private CompactHud? _hud;

private void ToggleCompact()
{
    if (_view == ViewMode.Full) EnterCompact(); else EnterFull();
}

private void EnterCompact()
{
    _hud ??= new CompactHud(Host.Settings, _compactHotkeyOk, onRestore: EnterFull, onMoved: SaveAnchor);
    _hud.PlaceAtTopRight(AnchorRight(), Top);
    _view = ViewMode.Compact; _tempFull = false;
    RenderHud();
    _hud.Show(); Hide();
    Host.Log.Info("UI", "view compact");
}

private void EnterFull()
{
    _view = ViewMode.Full; _tempFull = false;
    _hud?.Hide(); Show();
    Host.Log.Info("UI", "view full");
}

/// 利用者に見せる必要があるもの(問いかけ・復帰カード・確認・振り返り)を出す前に呼ぶ
private void ShowFullTemporarily(string why)
{
    if (_view != ViewMode.Compact || _tempFull) return;
    _tempFull = true; _hud?.Hide(); Show();
    Host.Log.Info("UI", $"view full (temporary: {why})");
}

/// それらが閉じたら呼ぶ(AutoHide の Tick、[このまま再開]、ダイアログの後)
private void EndTemporaryFull()
{
    if (!_tempFull) return;
    _tempFull = false; Hide(); _hud?.Show();
}
```
- WM_HOTKEY に `HotkeyCompactId` を足し、`ToggleCompact()` を呼ぶ。登録に失敗したら `_compactHotkeyOk = false`(HUD はクリック透過にしない)。
- `ShowAwayPrompt` / `ShowResumeCard` / `Window_Closing` の確認 / `EndFlowAsync` の最初で `ShowFullTemporarily(...)`。`AutoHide` の Tick、`CloseCard_Click`、`AwayNo_Click`、ダイアログの後で `EndTemporaryFull()`。
- `Render(StateSnapshot)` の最後で `if (_view == ViewMode.Compact) _hud?.Update(s, edgeKey);`。`EnterStartMode` で `_hud?.ShowIdle()`。
- `Window_Closing`: `_hud?.Close()` を足す(閉じ忘れでプロセスが残らないように)。

**CompactHud.cs(要点)**
```csharp
public sealed class CompactHud : Window
{
    private readonly DispatcherTimer _hover = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _clickThrough;
    // 中身: 左端の色帯(Border 4px)・状態の点・サブタスク名(最大14文字で省略)・経過時間・[⤢](Ctrl で触れるときだけ見せる)
    public CompactHud(AppSettings s, bool hotkeyOk, Action onRestore, Action<double, double> onMoved) { … AllowsTransparency = true; … }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h = new WindowInteropHelper(this).Handle;
        NativeMethodsPublic.ExcludeFromCapture(h);
        NativeMethodsPublic.SetExStyle(h, NativeMethodsPublic.GetExStyle(h) | NativeMethodsPublic.NoActivate | NativeMethodsPublic.ToolWindow);
        _hover.Tick += (_, _) => ApplyLook();
        _hover.Start();
    }
    private void ApplyLook()
    {
        var look = HudPolicy.Decide(_hotkeyOk, _s.CompactClickThrough, CursorOverMe(), CtrlDown(), _s.CompactOpacity, _s.CompactHoverOpacity);
        Opacity = look.Opacity;
        if (look.ClickThrough != _clickThrough) { SetClickThrough(look.ClickThrough); _clickThrough = look.ClickThrough; }
        RestoreButton.Visibility = look.ClickThrough ? Visibility.Collapsed : Visibility.Visible;
    }
}
```

## 4. 仕様書・記録の更新(実装と同じ変更で行う)

- 03 S-02: 既定は空、プレースホルダー、タスク名が空なら始められない。S-04: コンパクト表示(内容・透過・クリック透過・Ctrl で触る・F8・自動で一時的に戻す)。タイトル帯のボタン。
- 04: `task_categories` の draft_* / last_used_at、スキーマ v5。
- 05 F-01: 下書き(保存のタイミング、一覧の条件、自動入力の優先順)。
- 09 §9.4.1: ホットキーに F8(コンパクト表示)を追加。§9.4.6: コンパクト表示も画面共有から除外。
- Version 0.10A、AppInfo、plan(「要ローカル実機確認」に L-30〜L-33)。

## 5. 確認したいこと(実装前に利用者へ)

| Q | 内容 | この設計の既定 |
|---|------|----------------|
| Q1 | 縮める / 戻すのキーは F8 でよいか(起動中は F8 が他のアプリに届かなくなる) | F8。設定で変更可 |
| Q2 | R3「閉じるボタンの適切な場所」で気になったのは、(a) ✕ が小さく薄くて分かりにくい、(b) 作業中の「終了」と紛らわしい、(c) 位置そのもの(例: 左上にほしい、パネルの下にほしい)、のどれか | (a)(b) と解釈し、右上・標準の大きさ+「タスク完了」への改名 |
| Q3 | 下書きで一覧が増えるので、プルダウンから「一覧から外す」操作を付けるか | 付ける(右クリックメニュー「一覧から外す」) |
| Q4 | 異常終了でも開始準備の入力を失わないよう、入力の2秒後にも自動保存するか | しない(閉じる・開始・手順の決定の3か所で保存) |
| Q5 | コンパクト表示の既定の濃さ(通常 55%、マウスを重ねたとき 20%)とクリック透過の既定 | この値で始め、実際に使って調整する |

## 6. 実装の順序と確認方法

| 順 | 内容 | 確認 |
|----|------|------|
| 1 | Core `HudPolicy` + テスト | `dotnet test`(クラウドでも可) |
| 2 | Storage スキーマ v5、`TaskDraft`、`SaveDraft` / `LoadDraft`、`ListCategories` の変更 + テスト(移行テストの更新を含む) | `dotnet test` |
| 3 | R1 + R2 の画面(空から開始、プレースホルダー、開始ボタンの有効/無効、下書きの保存と自動入力) | `ui_render_probe` の画像、UI Automation(入力 → 閉じる → 再起動 → 一覧に出る → 選ぶと復元) |
| 4 | R3 タイトル帯のボタンと「タスク完了」 | 画像、`ui_close_flow_probe.ps1`(ボタン名を更新) |
| 5 | R4 `CompactHud` と切り替え・一時的に戻す・F8 | 画像(HUD 単体)、Win32 で拡張スタイル(WS_EX_TRANSPARENT / LAYERED)と表示の除外(0x11)を確認、UI Automation で F8 相当の切り替え(`ToggleCompact` をボタンから) |
| 6 | 仕様書・Version 0.10A・plan、持ち運び版の再作成(`scripts/publish-portable.ps1`) | — |

実機で利用者に見てもらう項目(plan の表に追加する):
- L-30: 起動時・タスク終了後に開始準備が空。タスク名が空だと始められない。
- L-31: 入力だけして閉じたタスクが、次の起動でプルダウンに出て、選ぶと入力が戻る。
- L-32: タイトル帯の ▭ – ✕ が分かりやすい位置・大きさか。「タスク完了」と取り違えないか。
- L-33: F8 / ▭ でコンパクト表示になり、下のアプリをクリック・閲覧できる。Ctrl を押しながら重ねると触れる。F9 で中断 → 再開すると復帰カードのために一時的に戻り、畳まれるとコンパクトに戻る。2枚のモニタ・拡大率違いでも位置がずれない。

## 7. 実装の結果(2026-10-06)

### 利用者の回答(§5)

| Q | 回答 | 反映 |
|---|------|------|
| Q1 | F8 でよい | F8(設定で変更可) |
| Q2 | (b) 作業中の「終了」と紛らわしい に近い | 作業中の「終了」を「**タスク完了**」に改名(ツールチップで「タスクを終えて振り返りを見る。アプリは閉じない」)。✕ は右上・標準の大きさ(46×34)で「アプリを閉じる」。✕ のホバーは落ち着いた赤み |
| Q3 | 「一覧から外す」も付ける | プルダウンの行の右クリック →「一覧から外す」(`archived=1`。記録は消さず、同じ名前を使うと戻る) |
| Q4 | 自動保存は不要。3か所の保存のみ | 開始・閉じる(開始準備のとき)・手順の決定 |
| Q5 | 提案の値でよい | 55% / 20% / クリック透過あり |

### 設計から変えた点

- **コンパクト表示から閉じる要求を受けたとき**: 設計では `Window_Closing` の中で元の表示に戻す想定だったが、WPF は `Closing` イベントの内側で `Show()` を呼ぶと例外にする(0.5B の `Close()` と同じ制約)。実機相当の確認で、「予期しないエラー」のダイアログが出てアプリが終了しなくなることが分かった。そこで、`Closing` を一度キャンセルし、イベントを抜けてから元の表示に戻し、そのあとで閉じる処理をやり直す方式にした(`ShowFullTemporarilyLater`)。キャンセルを選ぶとコンパクト表示に戻る。
- **一覧の条件**: 「終了した実行がある」から「実行したことがある(中止・中断中を含む)」に広げた(設計どおり)。既存テスト(中止した実行だけのタスクは出さない)は、この仕様変更に合わせて直した。
- **下書きの自動入力の優先順**: 手順は 登録した手順 → 下書き → 前回の実行。ほかの欄は 下書き → 既定値(最初の一手は、下書きが無ければ前回の実行)。
- **`EnsureCategory`**: 「一覧から外した」タスクを使ったら戻すため、`archived=0` に戻す処理を入れた(開始・手順の登録・下書きの保存のどれでも戻る)。

### 確認したこと

- 自動テスト 111 件(HudPolicy 8、下書き 8 など)。
- 本物のアプリでの一連の操作(UI Automation と Win32。試用データ): 空で起動・開始ボタンが無効 → 入力で有効 → 手順エディタで2手順を登録 → 開始せずに閉じる → 再起動で空から始まりプルダウンに「手順登録済み」「入力のみ」が出る → 選ぶと手順・プロジェクトが復元 → 開始 → コンパクト表示(フォーカスを奪わない、クリック透過、画面共有から除外 0x11、メインは隠れる)→ F8 相当(WM_HOTKEY)で戻る → F9 の中断・再開で復帰カードのため一時的に元の表示に戻り「このまま再開」でコンパクトに戻る → コンパクトのまま閉じる要求 → 確認ダイアログの前に元の表示に戻り、「いいえ」で中断して終了、「キャンセル」でコンパクトに戻る → タスク完了で振り返り → 空の開始準備に戻る。
- 見た目は `prototype/ui_render_probe` の画像(空の開始準備、タスクを選んだ状態、プルダウン、コンパクト表示の各状態)。
- **実際のマウス操作でしか確認できないもの**(利用者の確認待ち): 右クリックの「一覧から外す」、Ctrl を押しながらマウスを重ねたときの操作・ドラッグ移動、マウスを重ねたときの薄くなる動き、実際の F8 キー、2枚のモニタ・拡大率違いでの位置。
