---
name: release
description: Execute a SnippetLauncher release end-to-end — bump version, update CHANGELOG, tag, build self-contained zip, push, and create GitHub Release. Use when the user says "release", "publish", "new version", "deel met gebruikers", or similar.
---

# SnippetLauncher release

Automates the release procedure from `CLAUDE.md`. Always confirms the version bump with the user before doing anything destructive. Requires **Inno Setup 6** installed on the build machine (used in step 7b to produce the per-user installer asset).

## Step 0 — Confirm version

Run `git tag --list --sort=-v:refname | head -1` to get the latest tag.

Ask the user:
> "Is dit een PATCH (bugfix), MINOR (nieuwe feature) of MAJOR (breaking change)? Huidige versie is `<latest tag>`."

Compute the new version `vX.Y.Z` from their answer. Do not proceed without confirmation.

## Step 1 — Verify clean state

```
git status
```

If there are uncommitted changes that aren't part of this release, stop and ask the user how to proceed. Otherwise stage only the intentional changes.

## Step 2 — Bump version in Directory.Build.props

Edit `Directory.Build.props` and update all four properties to the new version:

- `<Version>X.Y.Z</Version>`
- `<AssemblyVersion>X.Y.Z.0</AssemblyVersion>`
- `<FileVersion>X.Y.Z.0</FileVersion>`
- `<InformationalVersion>X.Y.Z</InformationalVersion>`

## Step 3 — Update CHANGELOG.md

- Move everything under `## [Unreleased]` into a new `## [X.Y.Z] — YYYY-MM-DD` section (use today's date).
- Leave `## [Unreleased]` empty at the top for next iteration.
- Update comparison links at the bottom: change `[Unreleased]` to compare from the new tag, and add a `[X.Y.Z]` link.

If `## [Unreleased]` is empty, ask the user what should go in the changelog before continuing.

## Step 4 — Commit

Conventional commit subject. Include the version bump and changelog update in this commit.

```
git add Directory.Build.props CHANGELOG.md <other intentional files>
git commit -m "chore: release vX.Y.Z"
```

## Step 5 — Tag

```
git tag -a vX.Y.Z -m "vX.Y.Z - <one-line summary>"
```

Ask the user for the one-line summary if not obvious from the changelog.

## Step 6 — Build self-contained publish artifact

```
rm -rf publish/SnippetLauncher-win-x64
dotnet publish src/SnippetLauncher.App/SnippetLauncher.App.csproj \
  -c Release -r win-x64 --self-contained true \
  -o publish/SnippetLauncher-win-x64
```

If publish fails, stop. Do not push a broken release.

## Step 7 — Zip (PowerShell)

```
Compress-Archive -Path publish\SnippetLauncher-win-x64 \
  -DestinationPath publish\SnippetLauncher-vX.Y.Z-win-x64.zip -Force
```

## Step 7b — Build Inno Setup installer (PowerShell)

```
$Iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $Iscc)) {
  throw "Inno Setup 6 niet gevonden op $Iscc. Install van https://jrsoftware.org/isinfo.php"
}
& $Iscc "/DAppVersion=X.Y.Z" "installer\SnippetLauncher.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }
```

If `iscc.exe` is missing, **stop**. Do not push a release that's missing the
installer asset — second users will only see the zip and the whole installer
flow is invisible to them.

## Step 7c — Generate SHA256 checksums (PowerShell)

```
Get-FileHash `
  publish\SnippetLauncher-vX.Y.Z-win-x64.zip, `
  publish\SnippetLauncher-Setup-vX.Y.Z.exe `
  -Algorithm SHA256 |
  ForEach-Object { "$($_.Hash.ToLower())  $(Split-Path $_.Path -Leaf)" } |
  Set-Content -Encoding ascii publish\SHA256SUMS.txt
```

Without code-signing, this hash file is the only integrity anchor users have.

## Step 8 — Push commit and tag

```
git push origin master
git push origin vX.Y.Z
```

## Step 9 — GitHub Release

Extract the `## [X.Y.Z]` section from `CHANGELOG.md` and append the SHA256
block from `publish\SHA256SUMS.txt`. Upload all three assets:

```
$Sha = Get-Content publish\SHA256SUMS.txt -Raw
gh release create vX.Y.Z `
  publish\SnippetLauncher-vX.Y.Z-win-x64.zip `
  publish\SnippetLauncher-Setup-vX.Y.Z.exe `
  publish\SHA256SUMS.txt `
  --title "vX.Y.Z - <one-line summary>" `
  --notes "<changelog block>`n`n## SHA256 checksums`n``````n$Sha```````"
```

## After release

Report the release URL to the user (`gh release view vX.Y.Z --json url -q .url`).

## Failure handling

- If a step fails partway, **do not** retry blindly — report what failed and ask the user.
- Never use `--force`, `--no-verify`, or amend already-pushed commits without explicit user approval.
- If you tagged but the publish failed, you can delete the local tag with `git tag -d vX.Y.Z` (only if not yet pushed).
