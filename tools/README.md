# Tools

This directory contains maintainer scripts used during build/release workflows.

## Overview

### `Generate-AppIcon.ps1`

Creates a multi-size `.ico` file from an input image (PNG/JPG).

What it does:
- detects visible (non-transparent) bounds in the source image
- scales and centers the image for multiple icon sizes
- writes a Windows ICO containing PNG payloads for:
  - `16, 24, 32, 48, 64, 128, 256`

Usage:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Generate-AppIcon.ps1 `
  -InputImage .\Assets\Drauniav-logo.png `
  -OutputIco .\Assets\Drauniav.ico
```

Notes:
- Called automatically by the project build targets in `Drauniav.csproj`.
- Can also be run manually to regenerate the icon.

---

### `New-PortableRelease.ps1`

Builds reproducible portable release artifacts and optionally creates a GitHub Draft release.

What it does:
1. validates requested version against `Drauniav.csproj` (unless overridden)
2. runs `dotnet publish` (`self-contained`, `single-file`)
3. creates portable ZIP
4. validates ZIP by extracting and checking `Drauniav.exe`
5. writes SHA-256 checksum file
6. writes release notes (auto-generated, or copied from custom file)
7. optionally creates/recreates a Draft release via `gh`

Default output paths:
- `artifacts/publish/win-x64/`
- `artifacts/release/vX.Y.Z/drauniav-vX.Y.Z-win-x64-portable.zip`
- `artifacts/release/vX.Y.Z/checksums-vX.Y.Z.txt`
- `artifacts/release/vX.Y.Z/release-notes-vX.Y.Z.md`

Basic usage:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\New-PortableRelease.ps1 -Version 0.2.1
```

Create draft release on GitHub:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\New-PortableRelease.ps1 `
  -Version 0.2.1 `
  -CreateDraftRelease
```

Recreate existing draft/tag and local artifacts:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\New-PortableRelease.ps1 `
  -Version 0.2.1 `
  -CreateDraftRelease `
  -ReplaceExistingRelease `
  -OverwriteReleaseArtifacts
```

Key parameters:
- `-Version` (required): semantic version without `v`, e.g. `0.2.1`
- `-Runtime`: default `win-x64`
- `-Configuration`: default `Release`
- `-CreateDraftRelease`: upload assets and create draft in GitHub
- `-ReplaceExistingRelease`: delete existing release/tag before recreating
- `-OverwriteReleaseArtifacts`: recreate local release directory if it exists
- `-NotesFile`: use a custom markdown file for release notes
- `-AllowVersionMismatch`: skip strict check vs. `Drauniav.csproj`
- `-SkipPublish`: skip publish step and package existing publish output
- `-SkipZipValidation`: skip extract/check validation step

Prerequisites for draft creation:
- GitHub CLI installed (`gh`)
- authenticated (`gh auth status`)
- permissions for the target repository (`repo` scope)

## Recommended release workflow

1. Update version in `Drauniav.csproj`.
2. Commit and push version change.
3. Run `New-PortableRelease.ps1` with `-CreateDraftRelease`.
4. Verify assets and checksum in GitHub Draft.
5. Publish release manually in GitHub UI.
