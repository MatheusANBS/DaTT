<#
.SYNOPSIS
    DaTT Release Script
.DESCRIPTION
    Prepares CHANGELOG, bumps version, builds, publishes, packages installer and commits.
.EXAMPLE
    .\release.ps1
    .\release.ps1 -Version 1.1.0
#>

param(
    [string]$Version = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot

# ==============================================================================
# Bootstrap: PATH extras (dotnet + scoop)
# ==============================================================================

$sdkDotnetDir = "C:\Program Files\dotnet"
if ((Test-Path "$sdkDotnetDir\dotnet.exe") -and ($env:PATH -notlike "*$sdkDotnetDir*")) {
    $env:PATH = "$sdkDotnetDir;" + $env:PATH
}

$scoopShims = "$env:USERPROFILE\scoop\shims"
if ((Test-Path $scoopShims) -and ($env:PATH -notlike "*$scoopShims*")) {
    $env:PATH = "$scoopShims;" + $env:PATH
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "  FAIL  dotnet not found. Install .NET SDK from https://aka.ms/dotnet/download" -ForegroundColor Red
    exit 1
}

# ==============================================================================
# Bootstrap: gh CLI
# ==============================================================================

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Host ""
    Write-Host "  gh CLI not found. It is required to create GitHub Releases." -ForegroundColor Yellow
    $adminChoice = Read-Host "  Do you have admin rights on this machine? (Y/n)"

    if ($adminChoice -ne 'n' -and $adminChoice -ne 'N') {
        Write-Host "  Installing gh via winget..." -ForegroundColor Cyan
        winget install --id GitHub.cli --accept-package-agreements --accept-source-agreements
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  FAIL  winget install failed. Try manually: https://cli.github.com" -ForegroundColor Red
            exit 1
        }
        # Refresh PATH from machine/user environment after winget installs
        $env:PATH = [System.Environment]::GetEnvironmentVariable("PATH", "Machine") + ";" + `
                    [System.Environment]::GetEnvironmentVariable("PATH", "User")
    } else {
        Write-Host "  Installing via Scoop (no admin required)..." -ForegroundColor Cyan

        if (-not (Get-Command scoop -ErrorAction SilentlyContinue)) {
            Write-Host "  Installing Scoop..." -ForegroundColor Cyan
            Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser -Force -ErrorAction SilentlyContinue
            Invoke-RestMethod -Uri https://get.scoop.sh | Invoke-Expression
            if ($LASTEXITCODE -ne 0) {
                Write-Host "  FAIL  Scoop installation failed." -ForegroundColor Red
                exit 1
            }
            if ((Test-Path $scoopShims) -and ($env:PATH -notlike "*$scoopShims*")) {
                $env:PATH = "$scoopShims;" + $env:PATH
            }
        }

        scoop install gh
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  FAIL  scoop install gh failed." -ForegroundColor Red
            exit 1
        }
        if ((Test-Path $scoopShims) -and ($env:PATH -notlike "*$scoopShims*")) {
            $env:PATH = "$scoopShims;" + $env:PATH
        }
    }

    # Verify gh is now available
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        Write-Host "  FAIL  gh CLI still not found after installation. Restart the terminal and try again." -ForegroundColor Red
        exit 1
    }
    Write-Host "  OK  gh CLI installed." -ForegroundColor Green

    # Authenticate
    Write-Host ""
    Write-Host "  gh needs to be authenticated with your GitHub account." -ForegroundColor Yellow
    gh auth login
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  FAIL  gh auth login failed." -ForegroundColor Red
        exit 1
    }
    Write-Host "  OK  gh CLI authenticated." -ForegroundColor Green
}

function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "  >> $msg" -ForegroundColor Cyan
}

function Write-Ok([string]$msg) {
    Write-Host "  OK  $msg" -ForegroundColor Green
}

function Write-Fail([string]$msg) {
    Write-Host "  FAIL  $msg" -ForegroundColor Red
    exit 1
}

function Assert-LastExit([string]$step) {
    if ($LASTEXITCODE -ne 0) { Write-Fail "$step failed (exit $LASTEXITCODE)" }
}

# ==============================================================================
# 1. Version
# ==============================================================================

Write-Host ""
Write-Host "  DaTT Release Script" -ForegroundColor Magenta
Write-Host "  ===========================================" -ForegroundColor DarkGray

if (-not $Version) {
    $match = Select-String -Path "$Root\installer\DaTT.iss" -Pattern '#define MyAppVersion "([^"]+)"'
    $currentVersion = $match.Matches[0].Groups[1].Value
    Write-Host ""
    Write-Host "  Current version: $currentVersion" -ForegroundColor Yellow
    $Version = Read-Host "  Enter new version (e.g. 1.1.0)"
}

if (-not ($Version -match '^\d+\.\d+\.\d+$')) {
    Write-Fail "Invalid version format. Use MAJOR.MINOR.PATCH (e.g. 1.1.0)"
}

$today = Get-Date -Format "yyyy-MM-dd"
Write-Ok "Version: $Version  |  Date: $today"

# ==============================================================================
# 2. Prepare CHANGELOG
# ==============================================================================

Write-Step "Preparing CHANGELOG.md"

$changelogPath = "$Root\CHANGELOG.md"
$changelogContent = Get-Content $changelogPath -Raw -Encoding UTF8

$newHeader = "## [$Version] - $today"

if ($changelogContent -match [regex]::Escape($newHeader)) {
    Write-Host "  Section $newHeader already exists, skipping insert." -ForegroundColor Yellow
} else {
    $newSection = "`r`n$newHeader`r`n`r`n### Added`r`n`r`n- `r`n`r`n"
    $changelogContent = $changelogContent -replace "(?s)(---\s*)", "`$1$newSection"
    [System.IO.File]::WriteAllText($changelogPath, $changelogContent, [System.Text.Encoding]::UTF8)
    Write-Ok "Added placeholder section to CHANGELOG.md"
}

Write-Host ""
Write-Host "  CHANGELOG.md has been opened. Fill in the release notes for v$Version," -ForegroundColor Yellow
Write-Host "  then save and come back here." -ForegroundColor Yellow

$codeExe = Get-Command code -ErrorAction SilentlyContinue
if ($codeExe) {
    & code $changelogPath
} else {
    Start-Process notepad $changelogPath
}

Write-Host ""
Read-Host "  Press ENTER when you are done editing CHANGELOG.md"

$changelogNow = Get-Content $changelogPath -Raw -Encoding UTF8
if (-not ($changelogNow -match ('## \[' + [regex]::Escape($Version) + '\]'))) {
    Write-Fail "Could not find section ## [$Version] in CHANGELOG.md"
}

Write-Ok "CHANGELOG.md updated"

# ==============================================================================
# 3. Bump version in csproj and .iss
# ==============================================================================

Write-Step "Bumping version numbers"

$csprojPath = "$Root\src\DaTT.App\DaTT.App.csproj"
$issPath    = "$Root\installer\DaTT.iss"

$csproj = Get-Content $csprojPath -Raw -Encoding UTF8
$csproj = $csproj -replace '<Version>[^<]+</Version>',                    "<Version>$Version</Version>"
$csproj = $csproj -replace '<AssemblyVersion>[^<]+</AssemblyVersion>',    "<AssemblyVersion>$Version.0</AssemblyVersion>"
$csproj = $csproj -replace '<FileVersion>[^<]+</FileVersion>',            "<FileVersion>$Version.0</FileVersion>"
[System.IO.File]::WriteAllText($csprojPath, $csproj, [System.Text.Encoding]::UTF8)

$iss = Get-Content $issPath -Raw -Encoding UTF8
$iss = $iss -replace '#define MyAppVersion "[^"]+"', ("#define MyAppVersion `"" + $Version + "`"")
[System.IO.File]::WriteAllText($issPath, $iss, [System.Text.Encoding]::UTF8)

Write-Ok "Version bumped in csproj and DaTT.iss"

# ==============================================================================
# 4. Build
# ==============================================================================

Write-Step "Building solution"
& dotnet build "$Root\DaTT.sln" -c Release -v quiet
Assert-LastExit "dotnet build"
Write-Ok "Build succeeded"

# ==============================================================================
# 5. Publish
# ==============================================================================

Write-Step "Publishing win-x64 self-contained"
$publishDir = "$Root\publish\win-x64"
& dotnet publish "$Root\src\DaTT.App\DaTT.App.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -o $publishDir -v quiet
Assert-LastExit "dotnet publish"
Write-Ok "Published to $publishDir"

# ==============================================================================
# 6. Build Installer
# ==============================================================================

Write-Step "Building Inno Setup installer"

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) { Write-Fail "Inno Setup not found. Install from https://jrsoftware.org/isdl.php" }

& $iscc "$Root\installer\DaTT.iss"
Assert-LastExit "ISCC"

$installerPath = "$Root\installer\output\DaTT-Setup-$Version.exe"
if (-not (Test-Path $installerPath)) {
    Write-Fail "Expected installer not found: $installerPath"
}
Write-Ok "Installer: $installerPath"

# ==============================================================================
# 7. Git commit, tag and push
# ==============================================================================

Write-Step "Committing and tagging release"

Push-Location $Root

$gitStatus = & git status --porcelain 2>&1
if ($gitStatus) {
    & git add -A
    Assert-LastExit "git add"
    & git commit -m "release: v$Version"
    Assert-LastExit "git commit"
    Write-Ok "Committed"
} else {
    Write-Host "  Nothing to commit." -ForegroundColor Yellow
}

$tagName = "V$Version"
$existingTag = & git tag -l $tagName
if (-not $existingTag) {
    & git tag $tagName
    Assert-LastExit "git tag"
    Write-Ok "Tagged $tagName"
} else {
    Write-Host "  Tag $tagName already exists, skipping." -ForegroundColor Yellow
}

$pushChoice = Read-Host "  Push to origin? (Y/n)"
if ($pushChoice -ne 'n' -and $pushChoice -ne 'N') {
    & git push origin HEAD
    Assert-LastExit "git push HEAD"
    & git push origin $tagName
    Assert-LastExit "git push tag"
    Write-Ok "Pushed to origin"

    # ==============================================================================
    # 8. Create GitHub Release and upload installer
    # ==============================================================================

    Write-Step "Creating GitHub Release $tagName"

    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        Write-Host "  gh CLI not found - skipping GitHub Release." -ForegroundColor Yellow
        Write-Host "  Install from https://cli.github.com and re-run, or upload manually." -ForegroundColor Yellow
    } else {
        & gh release create $tagName $installerPath `
            --title "DaTT v$Version" `
            --notes-file "$Root\CHANGELOG.md"
        Assert-LastExit "gh release create"
        Write-Ok "GitHub Release created: $tagName  |  Asset: $(Split-Path $installerPath -Leaf)"
    }
}

Pop-Location

# ==============================================================================
# Done
# ==============================================================================

Write-Host ""
Write-Host "  ===========================================" -ForegroundColor DarkGray
Write-Host "  DaTT v$Version released successfully!" -ForegroundColor Green
Write-Host "  Installer: $installerPath" -ForegroundColor DarkGray
Write-Host ""
