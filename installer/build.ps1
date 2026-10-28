# 產生安裝檔：artifacts\ShelfSetup-<版本>.exe
# 用法：powershell -ExecutionPolicy Bypass -File installer\build.ps1 [-Version 1.0.1]
param([string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot
$publish = Join-Path $root "artifacts\publish"
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "找不到 Inno Setup，請先執行：winget install JRSoftware.InnoSetup" }

# 安裝檔要讓沒裝 .NET 的電腦也能用：這裡發佈含 Runtime 的版本（平常開發用的 Release 設定不含）
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
dotnet publish (Join-Path $root "src\Shelf") -c Release -o $publish -p:SelfContained=true -p:Version=$Version -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失敗" }

& $iscc /Q "/DAppVersion=$Version" (Join-Path $PSScriptRoot "Shelf.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup 編譯失敗" }

Get-Item (Join-Path $root "artifacts\ShelfSetup-$Version.exe") | ForEach-Object { "安裝檔：$($_.FullName)（$([int]($_.Length / 1MB)) MB）" }
