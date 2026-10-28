# Shelf

桌面暫存架：一顆浮動球，收集拖入的檔案、自動收集剪貼簿的文字與圖片，隨時拖出或複製使用。個人自用。

設計與實作細節見 [docs/開發文件.md](docs/開發文件.md)。

## 安裝

執行 `ShelfSetup-<版本>.exe`。不需要系統管理員權限、不需要另外裝 .NET（已包含在內）。

- 裝到 `%LOCALAPPDATA%\Programs\Shelf`，建立開始功能表捷徑，可在「設定 → 應用程式」解除安裝。
- 安裝時可勾選「開機時自動啟動」，之後也能在 Shelf 的設定裡切換。
- 升級：直接執行新版安裝檔，正在執行的 Shelf 會先被關掉再覆蓋。
- 解除安裝會保留 `%LOCALAPPDATA%\Shelf`（清單與圖片），重裝後還在。

## 開發

需要 Windows 10 / 11（x64）與 .NET 10 SDK。

```powershell
dotnet run --project src/Shelf                                         # 開發執行
powershell -ExecutionPolicy Bypass -File installer\build.ps1 -Version 1.0.1   # 產生安裝檔
```

安裝檔輸出到 `artifacts\ShelfSetup-<版本>.exe`（約 33 MB）。需要 Inno Setup 6（`winget install JRSoftware.InnoSetup`）。
`installer\ChineseTraditional.isl` 是 Inno Setup 官方原始碼庫的社群繁中翻譯（Inno Setup 沒有內建繁中）。

只想自己快速更新、不做安裝檔：從系統匣結束 Shelf 後，`dotnet publish src/Shelf -c Release -o "$env:LOCALAPPDATA\Programs\Shelf"`（不含 Runtime，約 300 KB）。

## 用法

| 操作 | 結果 |
|---|---|
| 點一下浮動球 / `Win+Alt+V` | 在球上方展開面板；熱鍵叫出時可以直接打字搜尋 |
| 拖動球 | 移到放開的位置並記住 |
| 拖檔案、文字、圖片到球或面板上 | 收進來 |
| 任何程式按 `Ctrl+C` | 自動收進來（密碼管理員標記的內容、捷徑 `.lnk` 不收） |
| 單擊項目 | 複製到剪貼簿，回原程式按 `Ctrl+V` 貼上 |
| 按住項目拖曳 | 拖到其他程式 |
| 搜尋框 `↑` `↓` `Enter` | 選取並複製，面板收起 |
| 右鍵項目 | 複製、釘選、開啟所在資料夾、另存新檔、刪除 |
| 點面板外面 / `Esc` | 收起面板 |

系統匣圖示右鍵可暫停收集、開設定、開資料夾、結束。

## 資料

全部存在 `%LOCALAPPDATA%\Shelf\`：`index.json`（清單）、`settings.json`（設定）、`store\`（圖片與長文字原檔）、`thumbs\`（縮圖）。結束 Shelf 後刪掉這個資料夾即可重置。

## 已知限制

- 以系統管理員身分執行的視窗，無法拖放進 Shelf（Windows 的 UIPI 限制）。改用單擊複製再貼上。
- 含 Win 鍵、且 Windows 自己已在用的組合（例如 `Win+S`），無法設為熱鍵。
- 兩個螢幕縮放比例不同時，未特別處理（目前使用環境兩螢幕皆 150%）。
