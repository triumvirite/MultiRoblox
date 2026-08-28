<#
.SYNOPSIS
  Build MultiRoblox into one standalone .exe and refresh the Desktop shortcut.

  Releases are cut automatically by GitHub Actions on every push to main
  (.github/workflows/release.yml) - this script is only for a quick local build.

.EXAMPLE
  ./publish.ps1
      Build MultiRoblox.exe and point a Desktop shortcut at it.

.EXAMPLE
  ./publish.ps1 -SkipShortcut
      Build only.
#>
param(
    [switch]$SkipShortcut
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dotnet = "$env:ProgramFiles\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

# --- build --------------------------------------------------------------
& $dotnet publish "$root\src\MultiRoblox.App\MultiRoblox.App.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

$exe = "$root\src\MultiRoblox.App\bin\Release\net8.0-windows\win-x64\publish\MultiRoblox.exe"
if (-not (Test-Path $exe)) { throw "exe not found at $exe" }
Write-Host "`n  exe: $exe" -ForegroundColor Green

# --- desktop shortcut --------------------------------------------------
if (-not $SkipShortcut) {
    $lnk = [Environment]::GetFolderPath('Desktop') + '\MultiRoblox.lnk'
    $ws = New-Object -ComObject WScript.Shell
    $s = $ws.CreateShortcut($lnk)
    $s.TargetPath = $exe
    $s.WorkingDirectory = Split-Path $exe
    $s.IconLocation = $exe
    $s.Save()
    Write-Host "  shortcut: $lnk" -ForegroundColor Green
}

Write-Host "`nDone." -ForegroundColor Green
