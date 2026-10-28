; Daizy 安裝程式（Inno Setup 6）
; 用 installer\build.ps1 產生，不要直接編譯：它會先發佈含 .NET Runtime 的版本，再傳入版本號。

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "Daizy"
#define AppExe "Daizy.exe"
; 改名前（1.2.0 以前）叫 Shelf：升級時要清掉舊的資料夾、捷徑、開機啟動值，並先關掉舊版
#define LegacyName "Shelf"
#define LegacyExe "Shelf.exe"
#define PublishDir "..\artifacts\publish"

[Setup]
; AppId 固定不變（改名也不變），升級時才會覆蓋同一個安裝，而不是裝成第二份
AppId={{6F2B8E4A-3C1D-4B7E-9A5F-2D8C1E0B7A63}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
; 裝在使用者自己的資料夾，不需要系統管理員權限（也避免以管理員執行時不能拖放的 UIPI 問題）
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
; 不沿用上次的安裝路徑：舊版裝在 Programs\Shelf，改名後搬到 Programs\Daizy
UsePreviousAppDir=no
DisableProgramGroupPage=yes
DisableDirPage=yes
OutputDir=..\artifacts
OutputBaseFilename=DaizySetup-{#AppVersion}
SetupIconFile=..\src\Shelf\Shelf.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; 升級或解除安裝時，若 Daizy 正在執行，先關掉再繼續，裝完再開回來
CloseApplications=force
RestartApplications=yes

[Languages]
Name: "zh_tw"; MessagesFile: "ChineseTraditional.isl"

[Tasks]
Name: "autostart"; Description: "開機時自動啟動 {#AppName}"

[InstallDelete]
; 改名前的安裝資料夾與開始功能表捷徑（使用者資料在 %LOCALAPPDATA%\Shelf，程式第一次啟動時自己搬，不在這裡刪）
Type: filesandordirs; Name: "{localappdata}\Programs\{#LegacyName}"
Type: files; Name: "{userprograms}\{#LegacyName}.lnk"

[Files]
; 發佈資料夾裡全部的檔案（含 .NET Runtime），除錯符號不用帶
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Registry]
; 改名前的開機啟動值
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "{#LegacyName}"; Flags: deletevalue
; 和程式內「開機時自動啟動」寫的是同一個值；解除安裝時一併移除
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; \
  ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "立即啟動 {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; 保險：Restart Manager 沒關掉的話強制結束，否則 exe 刪不掉
Filename: "{sys}\taskkill.exe"; Parameters: "/im {#AppExe} /f"; Flags: runhidden; RunOnceId: "KillDaizy"

[Code]
// 舊版 Shelf.exe 裝在另一個資料夾，Restart Manager 不會去關它；先結束，舊資料夾才刪得掉、資料夾也搬得動
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/im {#LegacyExe} /f', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

// 使用者資料（%LOCALAPPDATA%\Daizy：清單、設定、圖片）解除安裝時保留，重裝後清單還在。
// 要徹底清掉請手動刪除那個資料夾。
