[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z\.-]+)?$')]
    [string]$Version,

    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    [string]$Repository = 'geschke/drauniav',
    [string]$TargetBranch = 'main',
    [string]$NotesFile,

    [switch]$CreateDraftRelease,
    [switch]$ReplaceExistingRelease,
    [switch]$OverwriteReleaseArtifacts,
    [switch]$SkipPublish,
    [switch]$SkipZipValidation,
    [switch]$AllowVersionMismatch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [string]$WorkingDirectory
    )

    Write-Host ("> {0} {1}" -f $FilePath, ($Arguments -join ' '))

    if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        & $FilePath @Arguments
    }
    else {
        Push-Location $WorkingDirectory
        try {
            & $FilePath @Arguments
        }
        finally {
            Pop-Location
        }
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Get-GhExecutable {
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if ($gh) { return $gh.Source }

    $fallback = 'C:\Program Files\GitHub CLI\gh.exe'
    if (Test-Path -LiteralPath $fallback) {
        return $fallback
    }

    return $null
}

function Get-ProjectVersion {
    param([Parameter(Mandatory = $true)][string]$ProjectFile)

    [xml]$xml = Get-Content -LiteralPath $ProjectFile
    $versions = @()
    $versionNodes = $xml.SelectNodes('//Project/PropertyGroup/Version')
    foreach ($node in $versionNodes) {
        if ($node -and $node.InnerText -and $node.InnerText.Trim() -ne '') {
            $versions += $node.InnerText.Trim()
        }
    }
    $versions = @($versions | Select-Object -Unique)

    if ($versions.Count -gt 0) {
        return $versions[0]
    }

    return ''
}

function New-ReleaseNotes {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$Runtime,
        [Parameter(Mandatory = $true)][string]$ZipName,
        [Parameter(Mandatory = $true)][string]$ChecksumName,
        [Parameter(Mandatory = $true)][string]$OutputPath
    )

    $changes = @()
    $previousTag = ''

    try {
        $tagList = @(git -C $ProjectRoot tag --list 'v*' --sort=-version:refname)
        if ($LASTEXITCODE -eq 0 -and $tagList.Count -gt 0) {
            $previousTag = $tagList[0]
        }
    }
    catch {
        $previousTag = ''
    }

    if (-not [string]::IsNullOrWhiteSpace($previousTag) -and $previousTag -ne $Tag) {
        try {
            $changes = @(git -C $ProjectRoot log --oneline --no-merges "$previousTag..HEAD")
            if ($LASTEXITCODE -ne 0) {
                $changes = @()
            }
        }
        catch {
            $changes = @()
        }
    }

    $lines = @(
        "## Drauniav $Tag",
        "",
        "Portable release for Windows $Runtime.",
        "",
        "### Included assets",
        "- $ZipName",
        "- $ChecksumName (SHA256)",
        "",
        "### Highlights"
    )

    if ($changes.Count -gt 0) {
        foreach ($change in $changes) {
            if (-not [string]::IsNullOrWhiteSpace($change)) {
                $lines += "- $change"
            }
        }
    }
    else {
        $lines += "- Add highlights here."
    }

    $lines += @(
        "",
        "### Notes",
        "- Drauniav is a GUI for FFmpeg.",
        "- FFmpeg/FFprobe must be available in PATH or next to the executable."
    )

    Set-Content -LiteralPath $OutputPath -Encoding ascii -Value $lines
}

$scriptDir = Split-Path -Parent $PSCommandPath
$projectRoot = (Resolve-Path (Join-Path $scriptDir '..')).Path
$projectFile = Join-Path $projectRoot 'Drauniav.csproj'

if (-not (Test-Path -LiteralPath $projectFile)) {
    throw "Project file not found: $projectFile"
}

$tag = "v$Version"
$publishDir = Join-Path $projectRoot ("artifacts\publish\{0}" -f $Runtime)
$releaseDir = Join-Path $projectRoot ("artifacts\release\{0}" -f $tag)
$zipName = "drauniav-$tag-$Runtime-portable.zip"
$zipPath = Join-Path $releaseDir $zipName
$checksumName = "checksums-$tag.txt"
$checksumPath = Join-Path $releaseDir $checksumName
$notesName = "release-notes-$tag.md"
$notesPath = Join-Path $releaseDir $notesName
$validationDir = Join-Path $releaseDir '_zip_validation'

$projectVersion = Get-ProjectVersion -ProjectFile $projectFile
if (-not $AllowVersionMismatch -and -not [string]::IsNullOrWhiteSpace($projectVersion) -and $projectVersion -ne $Version) {
    throw "Project version is '$projectVersion' but requested release version is '$Version'. Update Drauniav.csproj or use -AllowVersionMismatch."
}

if (Test-Path -LiteralPath $releaseDir) {
    if (-not $OverwriteReleaseArtifacts) {
        throw "Release directory already exists: $releaseDir (use -OverwriteReleaseArtifacts to recreate)"
    }

    Remove-Item -LiteralPath $releaseDir -Recurse -Force
}

New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

if (-not $SkipPublish) {
    if (Test-Path -LiteralPath $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }

    Invoke-CheckedCommand -FilePath 'dotnet' -Arguments @(
        'publish',
        $projectFile,
        '-c', $Configuration,
        '-r', $Runtime,
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-o', $publishDir,
        '-nologo'
    )
}

if (-not (Test-Path -LiteralPath $publishDir)) {
    throw "Publish directory not found: $publishDir"
}

Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal

if (-not $SkipZipValidation) {
    if (Test-Path -LiteralPath $validationDir) {
        Remove-Item -LiteralPath $validationDir -Recurse -Force
    }

    Expand-Archive -LiteralPath $zipPath -DestinationPath $validationDir -Force

    $exePath = Join-Path $validationDir 'Drauniav.exe'
    if (-not (Test-Path -LiteralPath $exePath)) {
        throw "ZIP validation failed: Drauniav.exe not found after extraction."
    }

    Remove-Item -LiteralPath $validationDir -Recurse -Force
}

$sha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
@(
    "# Drauniav $tag checksums",
    "# Algorithm: SHA256",
    "",
    "$sha256  $zipName"
) | Set-Content -LiteralPath $checksumPath -Encoding ascii

if ([string]::IsNullOrWhiteSpace($NotesFile)) {
    New-ReleaseNotes -ProjectRoot $projectRoot -Tag $tag -Runtime $Runtime -ZipName $zipName -ChecksumName $checksumName -OutputPath $notesPath
}
else {
    if (-not (Test-Path -LiteralPath $NotesFile)) {
        throw "Notes file not found: $NotesFile"
    }

    Copy-Item -LiteralPath $NotesFile -Destination $notesPath -Force
}

$releaseUrl = ''
if ($CreateDraftRelease) {
    $ghExe = Get-GhExecutable
    if (-not $ghExe) {
        throw "GitHub CLI (gh) not found. Install gh or run without -CreateDraftRelease."
    }

    Invoke-CheckedCommand -FilePath $ghExe -Arguments @('auth', 'status')

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $ghExe release view $tag --repo $Repository *> $null
        $releaseExists = ($LASTEXITCODE -eq 0)
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($releaseExists) {
        if (-not $ReplaceExistingRelease) {
            throw "Release $tag already exists on $Repository. Use -ReplaceExistingRelease to recreate it."
        }

        Invoke-CheckedCommand -FilePath $ghExe -Arguments @('release', 'delete', $tag, '--repo', $Repository, '--yes', '--cleanup-tag')
    }

    Invoke-CheckedCommand -FilePath $ghExe -Arguments @(
        'release', 'create', $tag,
        $zipPath,
        $checksumPath,
        '--repo', $Repository,
        '--target', $TargetBranch,
        '--title', "Drauniav $tag",
        '--notes-file', $notesPath,
        '--draft'
    )

    $releaseUrl = (& $ghExe release view $tag --repo $Repository --json url --jq '.url')
}

$zipSizeBytes = (Get-Item -LiteralPath $zipPath).Length

Write-Host ''
Write-Host 'Release artifacts created:'
Write-Host ("  ZIP:       {0}" -f $zipPath)
Write-Host ("  Size:      {0} bytes" -f $zipSizeBytes)
Write-Host ("  SHA256:    {0}" -f $sha256)
Write-Host ("  Checksums: {0}" -f $checksumPath)
Write-Host ("  Notes:     {0}" -f $notesPath)
if (-not [string]::IsNullOrWhiteSpace($releaseUrl)) {
    Write-Host ("  Draft:     {0}" -f $releaseUrl)
}
