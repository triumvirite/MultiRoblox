<#
.SYNOPSIS
  Build MultiRoblox into one standalone .exe. Optionally zip it and cut a GitHub release.

.EXAMPLE
  ./publish.ps1
      Build MultiRoblox.exe and refresh the Desktop shortcut.

.EXAMPLE
  ./publish.ps1 -Release v1.1.0 -Notes "Fixed the server browser."
      Build, zip, tag v1.1.0, push the tag, and create the GitHub release with the zip attached.
#>
param(
    [string]$Release,                       # e.g. v1.1.0  (omit = just build locally)
    [string]$Notes = "",                    # release notes; ignored without -Release
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

if (-not $Release) { Write-Host "`nDone (local build only)." ; return }

# --- release ----------------------------------------------------------
if (-not $Release.StartsWith('v')) { $Release = "v$Release" }

$gh = (Get-Command gh -ErrorAction SilentlyContinue)
if (-not $gh) { throw "GitHub CLI (gh) not found - install it or cut the release manually." }

$zip = "$root\MultiRoblox-$Release-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $exe, "$root\README.md" -DestinationPath $zip
Write-Host "  zip: $zip ($([math]::Round((Get-Item $zip).Length/1MB,1)) MB)" -ForegroundColor Green

# tag
git -C $root tag $Release
git -C $root push origin $Release

if (-not $Notes) { $Notes = "MultiRoblox $Release - unzip and run MultiRoblox.exe (no install; .NET bundled). Windows 10/11 x64." }

gh release create $Release "$zip#MultiRoblox $Release (Windows x64)" `
    --repo (git -C $root remote get-url origin) `
    --title "MultiRoblox $Release" `
    --notes $Notes
if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }

Write-Host "`nReleased $Release." -ForegroundColor Green
