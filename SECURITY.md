# Security policy

FocusCoach is a hobby-grade prototype maintained by one person on a best-effort basis. There is **no support commitment and no guaranteed response time**, but security reports are welcome and will be looked at when possible.

## Reporting a vulnerability

Please use GitHub's **private vulnerability reporting** ("Security" tab -> "Report a vulnerability") instead of opening a public issue. Include the version (`VERSION.txt` in the ZIP, or the first line of `logs/app-*.log`), steps to reproduce, and the impact you expect.

## What data the app handles

FocusCoach records, **locally only**, things you do while a task is running: window titles, the process names of foreground windows, file and folder locations you opened, your notes and follow-ups. It makes no network connections and sends nothing anywhere.

- Data lives in `%LOCALAPPDATA%\FocusCoach` (or the `data` folder next to the portable launcher).
- It is **not encrypted**. Anyone with access to your Windows account or that folder can read it. Do not share the data folder or the log files when asking for help; redact them first.
- The main windows, dialogs, compact view and guide cards are excluded from screen capture, screen sharing and recordings by default. Exceptions: the short boot screen (no task data) and the tray icon menu and tooltip (status only). Older Windows 10 builds (before 2004) show these windows as black rectangles instead.

## Supported versions

Only the latest release is considered.

---

# セキュリティについて(日本語)

FocusCoach は個人が空き時間に作っているプロトタイプで、サポートや回答時間の保証はありません。脆弱性は、公開の Issue ではなく、GitHub の「Security」タブの **Private vulnerability reporting**(脆弱性を非公開で報告)から連絡してください。

記録(ウィンドウのタイトル、前面アプリ名、開いたファイルの場所、メモ、あとでやること)は **この PC の中だけ**に保存され、暗号化されていません。ネットワークには接続しません。データのフォルダ・ログは、そのままの形で他人に渡さないでください。
