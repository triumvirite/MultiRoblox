# Builds one standalone MultiRoblox.exe and drops a shortcut on your Desktop.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dotnet = "$env:ProgramFiles\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

& $dotnet publish "$root\src\MultiRoblox.App\MultiRoblox.App.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

$exe = "$root\src\MultiRoblox.App\bin\Release\net8.0-windows\win-x64\publish\MultiRoblox.exe"
if (-not (Test-Path $exe)) { throw "exe not found at $exe" }

$lnk = [Environment]::GetFolderPath('Desktop') + '\MultiRoblox.lnk'
$ws = New-Object -ComObject WScript.Shell
$s = $ws.CreateShortcut($lnk)
$s.TargetPath = $exe
$s.WorkingDirectory = Split-Path $exe
$s.IconLocation = $exe
$s.Save()

Write-Host ""
Write-Host "Done."
Write-Host "  exe:      $exe"
Write-Host "  shortcut: $lnk"
