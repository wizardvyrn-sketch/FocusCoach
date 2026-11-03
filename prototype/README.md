# prototype/

開発用のツール(Windows 専用。製品のコードではない)。 / Developer utilities (Windows only; not part of the product).

| ツール / Tool | 内容 / What it does |
|---|---|
| `ui_render_probe/` | アプリの各画面(開始準備、作業中、復帰カード、出口カード、再入場ガイド、ダイアログなど)を、画面に出さずに PNG にする(WPF の RenderTargetBitmap)。Renders every screen to PNG files without showing them (useful because screen capture is blocked by the app on purpose). |
| `icon_gen/` | アプリのアイコンを WPF の描画から作り、`app.ico`(16〜256px)と確認用 PNG を出す。Generates `app.ico` (16-256 px) and preview PNGs from WPF drawing code. |

```
dotnet run --project prototype/ui_render_probe -- <output-folder> [ja|en]
dotnet run --project prototype/icon_gen -- <output-folder>
```

`ui_render_probe` は画面共有からの除外(`WDA_EXCLUDEFROMCAPTURE`)のため、通常のスクリーンショットでは真っ黒になるアプリの画面を確認する目的で作った。保存先は隔離したフォルダ(環境変数 `FOCUSCOACH_DATA`)で、実際の記録は読み書きしない。
`ui_render_probe` writes into an isolated data folder (`FOCUSCOACH_DATA`) and never touches your real data.
