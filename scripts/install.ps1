#Requires -Version 5.1
<#
.SYNOPSIS
    Installs the KSR language toolchain.

.DESCRIPTION
    Builds all NuGet packages from source, then installs:
      - ksr       global CLI tool  (dotnet tool install -g KSR)
      - templates dotnet new templates (dotnet new install KSR.Templates)
      - VS Code extension (optional, if 'code' is on PATH)

.PARAMETER SkipVsCode
    Skip VS Code extension installation.

.PARAMETER Uninstall
    Remove the KSR toolchain instead of installing it.

.EXAMPLE
    .\install.ps1
    .\install.ps1 -SkipVsCode
    .\install.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [switch] $SkipVsCode,
    [switch] $Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── helpers ───────────────────────────────────────────────────────────────────

function Write-Step  ($msg) { Write-Host "  >> $msg" -ForegroundColor Cyan }
function Write-Ok    ($msg) { Write-Host "  OK $msg" -ForegroundColor Green }
function Write-Warn  ($msg) { Write-Host "  !! $msg" -ForegroundColor Yellow }
function Write-Fail  ($msg) { Write-Host "  FAIL $msg" -ForegroundColor Red; exit 1 }

function Invoke-Cmd {
    param([string] $Exe, [string[]] $ArgList)
    Write-Verbose "  > $Exe $($ArgList -join ' ')"
    & $Exe @ArgList
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "'$Exe $($ArgList -join ' ')' exited with code $LASTEXITCODE"
    }
}

# ── locate repo root (the directory that contains this script's parent) ───────

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent $ScriptDir

# ── uninstall ─────────────────────────────────────────────────────────────────

if ($Uninstall) {
    Write-Host ""
    Write-Host "Uninstalling KSR..." -ForegroundColor Magenta

    Write-Step "Removing ksr global tool"
    & dotnet tool uninstall -g KSR 2>$null
    if ($LASTEXITCODE -eq 0) { Write-Ok "ksr tool removed" }
    else                      { Write-Warn "ksr tool was not installed" }

    Write-Step "Removing KSR.Templates"
    & dotnet new uninstall KSR.Templates 2>$null
    if ($LASTEXITCODE -eq 0) { Write-Ok "KSR.Templates removed" }
    else                      { Write-Warn "KSR.Templates were not installed" }

    Write-Step "Removing VS Code extension"
    & code --uninstall-extension ksr-lang 2>$null
    if ($LASTEXITCODE -eq 0) { Write-Ok "VS Code extension removed" }
    else                      { Write-Warn "VS Code extension was not installed (or 'code' not found)" }

    Write-Host ""
    Write-Host "KSR uninstalled." -ForegroundColor Green
    exit 0
}

# ── install ───────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "Installing KSR language toolchain..." -ForegroundColor Magenta
Write-Host ""

# ── 1. Check .NET SDK ─────────────────────────────────────────────────────────

Write-Step "Checking .NET SDK"
$dotnetVersion = & dotnet --version 2>$null
if (-not $dotnetVersion) {
    Write-Fail ".NET SDK not found. Install from https://dotnet.microsoft.com/download"
}

$major = [int]($dotnetVersion -split '\.')[0]
if ($major -lt 8) {
    Write-Fail ".NET 8 or later is required (found $dotnetVersion). Install from https://dotnet.microsoft.com/download"
}
Write-Ok ".NET $dotnetVersion"

# ── 2. Build NuGet packages ───────────────────────────────────────────────────

$ArtifactsDir = Join-Path $RepoRoot "artifacts"
New-Item -ItemType Directory -Force -Path $ArtifactsDir | Out-Null

Write-Step "Building KSR.Core"
Invoke-Cmd dotnet @('pack', (Join-Path $RepoRoot 'KSR.Core.csproj'), '-c', 'Release', '-o', $ArtifactsDir, '-v', 'q', '--nologo')
Write-Ok "KSR.Core packed"

Write-Step "Building KSR.Build"
Invoke-Cmd dotnet @('pack', (Join-Path $RepoRoot 'sdk\KSR.Build\KSR.Build.csproj'), '-c', 'Release', '-o', $ArtifactsDir, '-v', 'q', '--nologo')
Write-Ok "KSR.Build packed"

Write-Step "Building KSR.Sdk"
Invoke-Cmd dotnet @('pack', (Join-Path $RepoRoot 'sdk\KSR.Sdk\KSR.Sdk.csproj'), '-c', 'Release', '-o', $ArtifactsDir, '-v', 'q', '--nologo')
Write-Ok "KSR.Sdk packed"

Write-Step "Building KSR.StdLib"
Invoke-Cmd dotnet @('pack', (Join-Path $RepoRoot 'sdk\KSR.StdLib\KSR.StdLib.csproj'), '-c', 'Release', '-o', $ArtifactsDir, '-v', 'q', '--nologo')
Write-Ok "KSR.StdLib packed"

Write-Step "Building KSR.Templates"
Invoke-Cmd dotnet @('pack', (Join-Path $RepoRoot 'sdk\KSR.Templates\KSR.Templates.csproj'), '-c', 'Release', '-o', $ArtifactsDir, '-v', 'q', '--nologo')
Write-Ok "KSR.Templates packed"

Write-Step "Building KSR CLI"
Invoke-Cmd dotnet @('pack', (Join-Path $RepoRoot 'KSR.csproj'), '-c', 'Release', '-o', $ArtifactsDir, '-v', 'q', '--nologo')
Write-Ok "KSR CLI packed"

# ── 3. Register local NuGet feed ──────────────────────────────────────────────

Write-Step "Registering local NuGet feed"
$feedName = 'ksr-local'
$existingSources = & dotnet nuget list source
if ($existingSources -match $feedName) {
    & dotnet nuget update source $feedName --source $ArtifactsDir | Out-Null
} else {
    & dotnet nuget add source $ArtifactsDir --name $feedName | Out-Null
}
Write-Ok "Feed '$feedName' → $ArtifactsDir"

# ── 4. Install ksr global tool ────────────────────────────────────────────────

Write-Step "Installing ksr global tool"
# Remove all KSR packages from NuGet cache so fresh local builds are always used
foreach ($pkg in @('ksr', 'ksr.core', 'ksr.build', 'ksr.sdk', 'ksr.stdlib', 'ksr.templates')) {
    $cacheDir = Join-Path $env:USERPROFILE ".nuget\packages\$pkg\0.1.0"
    if (Test-Path $cacheDir) { Remove-Item -Recurse -Force $cacheDir }
}
# Uninstall first in case a previous version is installed (ignore errors)
try { & dotnet tool uninstall -g KSR *>&1 | Out-Null } catch {}
Invoke-Cmd dotnet @('tool', 'install', '-g', 'KSR', '--add-source', $ArtifactsDir, '--version', '0.1.0')
Write-Ok "ksr tool installed"

# ── 5. Install dotnet new templates ──────────────────────────────────────────

Write-Step "Installing dotnet new templates"
try { & dotnet new uninstall KSR.Templates *>&1 | Out-Null } catch {}
$templatePkg = Join-Path $ArtifactsDir "KSR.Templates.0.1.0.nupkg"
Invoke-Cmd dotnet @('new', 'install', $templatePkg)
Write-Ok "KSR templates installed  (dotnet new ksr-console)"

# ── 6. Build & install VS Code extension (optional) ─────────────────────────

if (-not $SkipVsCode) {
    $vsixDir  = Join-Path $RepoRoot "vscode-extension"
    $vsix     = Join-Path $vsixDir  "ksr-lang-0.1.0.vsix"
    $codeCmd  = Get-Command 'code' -ErrorAction SilentlyContinue
    $npmCmd   = Get-Command 'npm'  -ErrorAction SilentlyContinue

    # Build the .vsix from source if npm is available
    if ($npmCmd) {
        Write-Step "Building VS Code extension"
        Push-Location $vsixDir
        Invoke-Cmd npm @('install', '--silent')
        Invoke-Cmd npm @('run', 'bundle', '--', '--minify')
        Invoke-Cmd npx @('vsce', 'package', '--out', 'ksr-lang-0.1.0.vsix', '--allow-missing-repository')
        Pop-Location
        Write-Ok "VS Code extension built"
    } else {
        Write-Warn "npm not found - using pre-built .vsix (if present)"
    }

    Write-Step "Installing VS Code extension"
    if ($codeCmd -and (Test-Path $vsix)) {
        Invoke-Cmd code @('--install-extension', $vsix)
        Write-Ok "VS Code extension installed"
    } elseif (-not $codeCmd) {
        Write-Warn "VS Code not found on PATH - skipping extension install"
        Write-Warn "To install manually:  code --install-extension $vsix"
    } else {
        Write-Warn ".vsix not found at $vsix - skipping extension install"
    }
}

# ── Done ──────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "KSR installed successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "  Get started:" -ForegroundColor White
Write-Host "    dotnet new ksr-console -n MyApp" -ForegroundColor Cyan
Write-Host "    cd MyApp" -ForegroundColor Cyan
Write-Host "    dotnet run" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Single-file mode:" -ForegroundColor White
Write-Host "    ksr hello.ksr" -ForegroundColor Cyan
Write-Host ""
