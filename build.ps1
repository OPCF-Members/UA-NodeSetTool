<#
.SYNOPSIS
    Builds, tests, packs, and optionally signs the OPC UA NodeSet Tool and library.

.DESCRIPTION
    Run signing-key.ps1 first to set the signing environment variables, then run this script.
    Version is determined automatically by Nerdbank.GitVersioning from version.json + git height.

        . .\signing-key.ps1
        .\build.ps1

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER OutputDir
    Directory for the .nupkg output. Defaults to ./build/nupkg.

.PARAMETER SkipTests
    Skip running tests.

.PARAMETER SkipSign
    Skip signing even when signing environment variables are set.
#>
param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "$PSScriptRoot/build/nupkg",
    [switch]$SkipTests,
    [switch]$SkipSign
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
function Write-Step([string]$msg) { Write-Host "`n>>> $msg" -ForegroundColor Cyan }

function Invoke-DotNet {
    $cmd = "dotnet $args"
    Write-Host $cmd -ForegroundColor DarkGray
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet command failed with exit code $LASTEXITCODE" }
}

# ---------------------------------------------------------------------------
# Detect signing configuration
# ---------------------------------------------------------------------------
$CanSign = (-not $SkipSign) `
    -and $env:SigningCertName `
    -and $env:SigningClientId `
    -and $env:SigningClientSecret `
    -and $env:SigningTenantId `
    -and $env:SigningVaultURL

if ($CanSign) {
    Write-Host "Signing credentials detected - packages will be signed." -ForegroundColor Green
} else {
    Write-Host "No signing credentials - packages will NOT be signed." -ForegroundColor Yellow
}

# ---------------------------------------------------------------------------
# Clean
# ---------------------------------------------------------------------------
Write-Step "Clean"
if (Test-Path "$PSScriptRoot/build") { Remove-Item "$PSScriptRoot/build" -Recurse -Force }
if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }

# ---------------------------------------------------------------------------
# Restore
# ---------------------------------------------------------------------------
Write-Step "Restore"
Invoke-DotNet restore

# ---------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------
Write-Step "Build ($Configuration)"
Invoke-DotNet build -c $Configuration --no-restore

# ---------------------------------------------------------------------------
# Test
# ---------------------------------------------------------------------------
if (-not $SkipTests) {
    Write-Step "Test"
    Invoke-DotNet test -c $Configuration --no-build --verbosity normal
}

# ---------------------------------------------------------------------------
# Pack
# ---------------------------------------------------------------------------
Write-Step "Pack"
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# Library NuGet package
Invoke-DotNet pack Opc.Ua.JsonNodeSet/Opc.Ua.JsonNodeSet.csproj `
    -c $Configuration --no-build `
    -o $OutputDir

# Tool NuGet package
Invoke-DotNet pack Opc.Ua.NodeSetTool/Opc.Ua.NodeSetTool.csproj `
    -c $Configuration --no-build `
    -o $OutputDir

# ---------------------------------------------------------------------------
# Sign NuGet packages (requires NuGetKeyVaultSignTool)
# ---------------------------------------------------------------------------
if ($CanSign) {
    Write-Step "Sign NuGet packages"

    # Ensure the signing tool is available
    $signTool = Get-Command NuGetKeyVaultSignTool -ErrorAction SilentlyContinue
    if (-not $signTool) {
        Write-Host "Installing NuGetKeyVaultSignTool..." -ForegroundColor DarkGray
        dotnet tool install --global NuGetKeyVaultSignTool
    }

    $timestampUrl = if ($env:SigningURL) { $env:SigningURL } else { "http://timestamp.digicert.com" }

    Get-ChildItem "$OutputDir/*.nupkg" | ForEach-Object {
        Write-Host "  Signing $($_.Name)" -ForegroundColor DarkGray
        NuGetKeyVaultSignTool sign $_.FullName `
            -kvu $env:SigningVaultURL `
            -kvc $env:SigningCertName `
            -kvt $env:SigningTenantId `
            -kvi $env:SigningClientId `
            -kvs $env:SigningClientSecret `
            -tr  $timestampUrl `
            -td  sha256
        if ($LASTEXITCODE -ne 0) { throw "NuGet package signing failed for $($_.Name)" }
    }
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Step "Done"
Write-Host "Packages:"
Get-ChildItem "$OutputDir/*.nupkg" | ForEach-Object { Write-Host "  $_" }
Write-Host ""
Write-Host "Install the tool locally with:"
Write-Host "  dotnet tool install --global --add-source $OutputDir Opc.Ua.NodeSetTool" -ForegroundColor DarkGray
