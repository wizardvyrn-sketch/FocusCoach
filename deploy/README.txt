FocusCoach - portable version / 持ち運び版
==========================================

English first, 日本語は下にあります (Japanese follows below).

##########################################################################
 ENGLISH
##########################################################################

This folder is all you need. No .NET or anything else has to be installed (everything is bundled in the "app" folder).
No administrator rights are needed.

[Requirements]
  Windows 10 / 11 (64-bit). A standard setup is enough.
  Disk space: about 170 MB (the size of this folder).

[Start]
  Double-click FocusCoach.cmd.
    Your data is stored per Windows user in %LOCALAPPDATA%\FocusCoach (on each PC).
  Double-click FocusCoach-portable.cmd (to carry it around).
    Your data is stored in the "data" folder next to it. Take the whole drive with you and the data follows.
    - It cannot be used on a drive that is write-protected (e.g. a locked USB stick). Use FocusCoach.cmd instead.
    - Do not open the same "data" folder from several PCs at the same time.
  Opening app\FocusCoach.exe directly also works (it then uses the same data location as FocusCoach.cmd).

[How to use (first time)]
  1. Type a task name (or pick one with the arrow) and press "Start focusing".
  2. When an interruption comes (a question, a call, a chat...), press the "Handle interruption" button (the bookmark icon) or F9.
     Nothing to type. A bookmark is placed where you are. Press F10 to jot down "where I was / my next step".
  3. When you are done, press F9 again (or "Back to work"). The "Close interruption" card opens.
     - Press Enter to close it with nothing written (pressing F9 once more does the same).
     - If something is left over, choose "Something is left to follow up" and write one line and a due date.
     - If you are still busy, press Esc ("Still handling it").
  4. Your original work window comes to the front, and a "Back to your task" guide stays for 40 seconds, then disappears by itself.
     What you do in that time is up to you (a deep breath, some tea, tidy your notes). Press "Back to work now" to skip it.
  5. "Follow-ups" are listed from the counter below the mini panel or from the tray menu. Check an item to mark it done.
  - Minimizing (-) sends the app to the system tray instead of the taskbar. If you cannot see the icon, look under the "^" arrow at the right of the taskbar.
    A left click on the icon brings the window back. Starting FocusCoach a second time also brings the running one forward.
  - The language can be switched between Japanese and English with the "JA / EN" button in the title bar, the tray menu "Language / 言語",
    or Ctrl+Alt+F8 (it is remembered for next time; at first it follows your Windows language).
  - F8 shrinks the app to a small translucent strip (compact view). Hold Ctrl while pointing at it to use it.

[Things to know first]
  - It starts in "Practice mode" (it does not move your windows). Untick it only when you really want windows to be minimized and restored.
  - F9 (interruption), F10 (memo) and F8 (compact view) may conflict with other apps. If a key cannot be registered, the app tells you; every action is also available by button.
  - The main windows, dialogs, the compact view and the guide cards do not appear in screen sharing or recordings (to protect task names and notes).
    Exceptions: the short boot screen (it shows no task data) and the tray icon menu / tooltip (status only).

[If you get a warning]
  - "Windows protected your PC" (SmartScreen): the program is not code-signed.
    Choose "More info" -> "Run anyway" only if you trust the download (check the SHA-256 on the release page).
    On a company PC, follow your security policy.
  - If antivirus or an execution policy (e.g. AppLocker) blocks it, ask your IT administrator for permission.
  - If running from removable drives is forbidden, copy the folder to the PC's own disk.

[Changing settings]
  Close FocusCoach, then open settings.json in the data folder with Notepad.
  For example ReentryGuideSec (seconds of the guide; 0 turns it off), ReentryGuideMinInterruptSec (shorter interruptions skip the guide),
  ReentryGuideMessage (the line of text) and Language (auto / ja / en). Hotkeys are virtual-key codes (F9 = 120).
  The full table is in the README on GitHub (section "Settings").

[Where your data is]
  Standard launcher: %LOCALAPPDATA%\FocusCoach  (focuscoach.db, sessions, logs, settings.json)
  Portable launcher: the "data" folder in this folder
  The data can contain window titles and file locations. Do not hand it to other people as it is.

[Uninstall]
  1. Quit FocusCoach (tray icon -> "Quit FocusCoach", or the X button).
  2. Delete this folder. There is no installer: no registry entries, no startup entry and no shortcuts are created.
  3. Your data is kept separately. To erase it too, delete %LOCALAPPDATA%\FocusCoach (standard launcher) or the "data" folder (portable launcher).
     Copy it somewhere first if you may want it later.

[Third-party materials]
  The boot-screen fonts Oxanium and Share Tech Mono are bundled under the SIL Open Font License 1.1 (see *-OFL.txt in app\Fonts).
  Other components and their licenses: THIRD-PARTY-NOTICES.txt. FocusCoach itself: LICENSE.txt (MIT).

[Version]
  See VERSION.txt. It is also in the first line of logs\app-*.log in the data folder.


##########################################################################
 日本語
##########################################################################

このフォルダだけで動きます。.NET など他のソフトのインストールは不要です(必要なものは app フォルダに同梱)。
管理者権限も不要です。

【必要な環境】
  Windows 10 / 11 (64bit)。標準のセットアップのままで動きます。
  ディスクの空き: 約 170MB(このフォルダの大きさ)。

【起動】
  FocusCoach.cmd をダブルクリック。
    記録は、そのWindowsユーザーの %LOCALAPPDATA%\FocusCoach に保存されます(端末ごと)。
  FocusCoach-portable.cmd をダブルクリック(持ち運び用)。
    記録は、このフォルダの data フォルダに保存されます。ドライブごと持ち歩けば、記録もついてきます。
    ・書き込みできないドライブ(USBの書き込み禁止など)では使えません。その場合は FocusCoach.cmd を使ってください。
    ・同じ data フォルダを、複数の端末で同時に開かないでください。
  app\FocusCoach.exe を直接開いても動きます(その場合は通常版と同じ保存先になります)。

【使い方(はじめての方へ)】
  1. タスク名を入れて(または ▾ で選んで)「集中を始める」を押します。
  2. 割り込み(問い合わせ・相談・電話など)が来たら、「割り込み対応」ボタン(しおりの絵)か F9 を押します。
     入力は要りません。今の場所に「しおり」を挟んだ状態になり、F10 で「どこまで / 次の一手」をメモできます。
  3. 対応が済んだら、もう一度 F9(または「元の作業へ」)を押します。「割り込みを閉じる」カードが出ます。
     ・何も書かずに Enter で閉じられます(もう一度 F9 でも同じ)。
     ・あとで続きがあるときは「あとでやることが残る」を選び、1行と期限を書きます。
     ・まだ対応が続くときは Esc(「まだ対応を続ける」)。
  4. 元の作業の画面が前に出て、「元の作業に戻ります」の案内が 40 秒出たあと、自動で消えます。
     その間に何をするかは自由です(深呼吸、お茶、メモの整理など)。すぐ戻るときは「今すぐ戻る」。
  5. 「あとでやること」は、ミニパネルの下やタスクトレイのメニューから一覧で見られます。チェックで完了にします。
  ・最小化(–)するとタスクバーではなくタスクトレイに入ります。アイコンが見えないときは、タスクバー右の「^」の中を見てください。
    アイコンの左クリックで元に戻ります。もう一度 FocusCoach を起動しても、起動中のものが出てきます。
  ・表示は日本語と英語(English)に切り替えられます。タイトル帯の「JA / EN」ボタン、タスクトレイのメニュー「Language / 言語」、
    またはキー Ctrl+Alt+F8 で切り替わります(次回の起動でも保たれます。最初は OS の言語に合わせます)。
  ・F8 で小さな半透明の表示(コンパクト表示)になります。Ctrl を押しながらマウスを重ねると操作できます。

【最初に確認すること】
  ・起動時は「練習モード」(ウィンドウを動かさない)になっています。実際にウィンドウを最小化したいときだけ、チェックを外します。
  ・F9(割り込み対応)・F10(メモ)・F8(コンパクト表示)は、他のアプリと競合することがあります。登録できないときは画面に表示されます。
  ・主なウィンドウ・ダイアログ・コンパクト表示・案内カードは、画面共有・録画に映りません(タスク名やメモを守るため)。
    例外: 起動時の短い画面(作業の内容は出ません)と、タスクトレイのアイコンのメニュー・ツールチップ(状態だけ)。

【警告が出たときは】
  ・「Windows によって PC が保護されました」(SmartScreen): 署名をしていないためです。
    ダウンロードしたものに間違いがないとき(リリースのページの SHA-256 と照合)だけ、「詳細情報」→「実行」で起動できます。
    会社の端末では、セキュリティの規程に従ってください。
  ・ウイルス対策や実行制限(AppLocker など)で止められたときは、IT管理者に許可を依頼してください。
  ・リムーバブルドライブからの実行が禁止されている端末では、フォルダを端末のディスクにコピーして使ってください。

【設定を変えるとき】
  FocusCoach を閉じてから、記録の保存先の settings.json をメモ帳で開いて書き換えます。
  例: ReentryGuideSec(案内の秒数。0 で出さない)、ReentryGuideMinInterruptSec(これより短い割り込みでは出さない)、
  ReentryGuideMessage(案内の一言)、Language(auto / ja / en)。ホットキーは仮想キーコードです(F9 = 120)。
  項目の一覧は、GitHub の README の「Settings」の節にあります(英語)。

【記録の場所】
  通常版: %LOCALAPPDATA%\FocusCoach(focuscoach.db、sessions、logs、settings.json)
  持ち運び版: このフォルダの data
  記録には、ウィンドウのタイトルやファイルの場所が含まれることがあります。そのままの形で他人に渡さないでください。

【アンインストール】
  1. FocusCoach を終了します(タスクトレイのアイコン →「アプリを閉じる」、または ✕ ボタン)。
  2. このフォルダを削除します。インストーラーは使わないので、レジストリ・スタートアップ・ショートカットは作られていません。
  3. 記録は別の場所にあります。記録も消すときは、%LOCALAPPDATA%\FocusCoach(通常版)または data フォルダ(持ち運び版)を削除します。
     あとで使うかもしれないときは、先に別の場所へコピーしてください。

【第三者の素材】
  起動画面の書体 Oxanium・Share Tech Mono は SIL Open Font License 1.1 で同梱しています(app\Fonts の *-OFL.txt)。
  そのほかの部品とライセンスは THIRD-PARTY-NOTICES.txt、FocusCoach 本体は LICENSE.txt(MIT)です。

【バージョン】
  VERSION.txt を見てください。起動時の記録(logs\app-*.log の1行目)にも出ます。
