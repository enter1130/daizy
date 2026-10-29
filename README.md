# Daizy

剪貼簿暫存架：按 Win+V 叫出（取代 Windows 的剪貼簿紀錄），點一下就貼上；也收集拖入的檔案，可隨時拖出。閒置時只佔約 1 MB 記憶體。

設計與實作細節見 [docs/開發文件.md](docs/開發文件.md)，版本紀錄見 [CHANGELOG.md](CHANGELOG.md)。

## 安裝

執行 `DaizySetup-<版本>.exe`。不需要系統管理員權限、不需要另外裝 .NET（已包含在內）。

- 裝到 `%LOCALAPPDATA%\Programs\Daizy`，建立開始功能表捷徑，可在「設定 → 應用程式」解除安裝。
- 安裝時可勾選「開機時自動啟動」，之後也能在 Daizy 的設定裡切換。
- 升級：直接執行新版安裝檔，正在執行的 Daizy 會先被關掉再覆蓋。
- 解除安裝會保留 `%LOCALAPPDATA%\Daizy`（清單與圖片），重裝後還在。

## 開發

需要 Windows 10 / 11（x64）與 .NET 10 SDK。

```powershell
dotnet run --project src/Shelf                                         # 開發執行
powershell -ExecutionPolicy Bypass -File installer\build.ps1 -Version 1.3.0   # 產生安裝檔
```

安裝檔輸出到 `artifacts\DaizySetup-<版本>.exe`（約 33 MB）。需要 Inno Setup 6（`winget install JRSoftware.InnoSetup`）。
`installer\ChineseTraditional.isl` 是 Inno Setup 官方原始碼庫的社群繁中翻譯（Inno Setup 沒有內建繁中）。

只想自己快速更新、不做安裝檔：從系統匣結束 Daizy 後，`dotnet publish src/Shelf -c Release -o "$env:LOCALAPPDATA\Programs\Daizy"`（不含 Runtime，約 300 KB）。

## 用法

| 操作 | 結果 |
|---|---|
| `Win+V`（可在設定改成自訂熱鍵） | 在文字游標旁開啟，直接打字搜尋 |
| 單擊項目 / `Enter` | 貼到原本的程式（只想複製：右鍵 →「複製」） |
| 點分類（全部 · 文字 · 圖片 · 檔案） | 篩選 |
| 任何程式按 `Ctrl+C` | 自動收進來（密碼管理員標記的內容、捷徑 `.lnk` 不收） |
| 拖檔案、文字、圖片到面板（或浮動球）上 | 收進來 |
| 按住項目拖曳 | 拖到其他程式 |
| 右鍵項目 | 複製、釘選、開啟所在資料夾、另存新檔、刪除 |
| 點面板外面 / `Esc` | 收起面板 |

浮動球預設隱藏，可在設定或系統匣選單打開。系統匣圖示右鍵可暫停收集、開設定、開資料夾、結束。

## 資料

全部存在 `%LOCALAPPDATA%\Daizy\`：`index.json`（清單）、`settings.json`（設定）、`store\`（圖片與長文字原檔）、`thumbs\`（縮圖）。結束 Daizy 後刪掉這個資料夾即可重置。

## 已知限制

- 以系統管理員身分執行的視窗（Windows 的 UIPI 限制）：無法拖放進 Daizy；點一下只會複製、不會自動貼上，要自己按 `Ctrl+V`。
- 含 Win 鍵、且 Windows 自己已在用的組合（例如 `Win+S`），無法設為自訂熱鍵（`Win+V` 例外，用專門的方式取代）。
- 注音 / 拼音輸入法開著時，搜尋框打的字先停在選字框，`Enter` 是確認選字。
- 兩個螢幕縮放比例不同時，未特別處理（只在兩螢幕同為 150% 的環境測過）。

## 授權

[MIT](LICENSE)
