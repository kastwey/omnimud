<#
.SYNOPSIS
    Builds the distributable files of one version of Omnimud.

.DESCRIPTION
    1. Checks that -Version is the version declared in Directory.Build.props (the single source of the version).
    2. Restores, builds in Release with warnings as errors, and runs every test.
    3. Publishes with the portable-win-x64 profile into a CLEAN folder (so nothing of a previous build, and never
       a data\ folder, can end up in the package).
    4. Writes Omnimud-<version>-win-x64-portable.zip and its SHA-256.
    5. If Inno Setup (ISCC.exe) is available, builds the per-user installer and its SHA-256 too.

    Nothing is uploaded, tagged or pushed from here: see README.md, "Publicar una versión".

.EXAMPLE
    .\tools\release.ps1 -Version 2.0.0

.EXAMPLE
    .\tools\release.ps1                    # the version of Directory.Build.props
    .\tools\release.ps1 -SkipTests         # only to try the packaging; never for a real release
    .\tools\release.ps1 -CI                # in GitHub Actions: excludes the tests that need an interactive desktop
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutputDirectory,
    [switch]$SkipTests,
    [switch]$CI,
    [switch]$NoInstaller,
    [switch]$RequireInstaller
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'Omnimud.sln'
$uiProject = Join-Path $root 'src\Omnimud.UI\Omnimud.UI.csproj'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts\release' }

function Invoke-Step([string]$Title, [scriptblock]$Action) {
    Write-Host ''
    Write-Host "== $Title" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "$Title failed (exit code $LASTEXITCODE)." }
}

function Write-Sha256([string]$File) {
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $File).Hash.ToLowerInvariant()
    # Same format as sha256sum, so it can be checked with "sha256sum -c" as well as by eye.
    $line = "$hash *$(Split-Path -Leaf $File)"
    Set-Content -LiteralPath "$File.sha256" -Value $line -Encoding ascii
    Write-Host "SHA-256  $line"
}

function Find-Iscc {
    $command = Get-Command iscc -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    return $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}

# ── 1. Version ───────────────────────────────────────────────────────────────
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
$declared = ([string]$props.Project.PropertyGroup.Version).Trim()
if (-not $declared) { throw 'Directory.Build.props has no <Version>.' }
if (-not $Version) { $Version = $declared }
$Version = $Version.Trim().TrimStart('v', 'V')
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.\-]+)?$') { throw "'$Version' is not a version like 2.1.0 or 2.1.0-beta.1." }
if ($Version -ne $declared) {
    throw "The version asked for ($Version) is not the one in Directory.Build.props ($declared). Change <Version> there first: it is what the program shows in 'About' and sends in the update check."
}
Write-Host "Omnimud $Version" -ForegroundColor Green

# The output folder is wiped in step 3, so it must never be the repository (or a parent of it) nor a folder where
# somebody plays: a portable Omnimud keeps the user's characters and triggers in data\ next to the executable.
# Checked here, before the long build, so that a wrong path fails at once.
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\', '/')
if ("$root\".StartsWith("$OutputDirectory\", [StringComparison]::OrdinalIgnoreCase)) {
    throw "-OutputDirectory ($OutputDirectory) is the repository or contains it. Refusing to delete it."
}
if (Test-Path -LiteralPath $OutputDirectory) {
    $userData = Get-ChildItem -LiteralPath $OutputDirectory -Recurse -Force -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in 'omnimud.db', 'master.key' } | Select-Object -First 1
    if ($userData) {
        throw "-OutputDirectory ($OutputDirectory) contains user data ($($userData.FullName)). Refusing to delete it: choose an empty folder."
    }
}

# ── 2. Build and test ────────────────────────────────────────────────────────
Invoke-Step 'Restore' { dotnet restore $solution }
Invoke-Step 'Build (Release, warnings are errors)' { dotnet build $solution -c Release --no-restore -warnaserror -nologo }
if ($SkipTests) {
    Write-Warning 'Tests skipped. Do not publish what comes out of this run.'
} else {
    $testArguments = @($solution, '-c', 'Release', '--no-build', '-nologo')
    # A CI runner does not guarantee an interactive desktop: see tests\Omnimud.UI.Tests\TestCategories.cs
    if ($CI) { $testArguments += @('--filter', 'Category!=InteractiveDesktop') }
    Invoke-Step 'Test' { dotnet test @testArguments }
}

# ── 3. Publish into a clean folder ───────────────────────────────────────────
if (Test-Path -LiteralPath $OutputDirectory) { Remove-Item -LiteralPath $OutputDirectory -Recurse -Force }
$publishDirectory = Join-Path $OutputDirectory 'Omnimud'
New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null
# PublishDir must end with a separator; the doubled one keeps the closing quote from being escaped.
Invoke-Step 'Publish (portable-win-x64)' {
    dotnet publish $uiProject -p:PublishProfile=portable-win-x64 "-p:PublishDir=$publishDirectory\\" -nologo
}

Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $publishDirectory 'LICENSE.txt')
Get-ChildItem -LiteralPath $publishDirectory -Recurse -Filter '*.pdb' | Remove-Item -Force

# What must be there, and what must never be.
$expected = @('Omnimud.exe', 'LICENSE.txt', 'docs\manual.es.html', 'docs\manual.en.html', 'docs\API_LUA.md', 'dlls\x64', 'sounds')
foreach ($item in $expected) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $item))) { throw "The published folder lacks '$item'." }
}
$forbidden = @('data', 'master.key') + @(Get-ChildItem -LiteralPath $publishDirectory -Recurse -Include '*.db', '*.db-wal', '*.db-shm', 'master.key' | ForEach-Object { $_.FullName })
foreach ($item in $forbidden) {
    $path = if ([IO.Path]::IsPathRooted($item)) { $item } else { Join-Path $publishDirectory $item }
    if (Test-Path -LiteralPath $path) { throw "User data must never be packaged, and '$path' is in the published folder." }
}
$productVersion = (Get-Item -LiteralPath (Join-Path $publishDirectory 'Omnimud.exe')).VersionInfo.ProductVersion
if (-not $productVersion.StartsWith($Version)) { throw "Omnimud.exe says version '$productVersion', not $Version." }

# ── 4. Portable zip ──────────────────────────────────────────────────────────
$zip = Join-Path $OutputDirectory "Omnimud-$Version-win-x64-portable.zip"
Write-Host ''
Write-Host "== Zip" -ForegroundColor Cyan
# The zip holds one folder, "Omnimud": unzipping never scatters files around.
Compress-Archive -LiteralPath $publishDirectory -DestinationPath $zip -CompressionLevel Optimal
Write-Sha256 $zip

# ── 5. Installer (optional) ──────────────────────────────────────────────────
if (-not $NoInstaller) {
    $iscc = Find-Iscc
    if ($iscc) {
        $script = Join-Path $PSScriptRoot 'installer\omnimud.iss'
        Invoke-Step 'Installer (Inno Setup)' {
            & $iscc /Qp "/DAppVersion=$Version" "/DSourceDir=$publishDirectory" "/DOutputDir=$OutputDirectory" $script
        }
        $setup = Join-Path $OutputDirectory "Omnimud-$Version-win-x64-setup.exe"
        if (-not (Test-Path -LiteralPath $setup)) { throw "Inno Setup did not produce $setup." }
        Write-Sha256 $setup
    } elseif ($RequireInstaller) {
        throw 'Inno Setup 6 (ISCC.exe) was not found and -RequireInstaller was given.'
    } else {
        Write-Warning 'Inno Setup 6 (ISCC.exe) was not found: only the portable zip was built. Install it from https://jrsoftware.org/isinfo.php to get the installer too.'
    }
}

Write-Host ''
Write-Host "Done. Files in $OutputDirectory" -ForegroundColor Green
Get-ChildItem -LiteralPath $OutputDirectory -File | ForEach-Object { '  {0}  ({1:N1} MB)' -f $_.Name, ($_.Length / 1MB) }
