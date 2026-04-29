# SnippetLauncher — Project Notes for Claude

## Releases & Versioning

This project uses **Semantic Versioning** (`MAJOR.MINOR.PATCH`):

- **PATCH** (`v1.0.0` → `v1.0.1`) — bugfixes, no behavior change for users
- **MINOR** (`v1.0.1` → `v1.1.0`) — new feature, backwards compatible
- **MAJOR** (`v1.1.0` → `v2.0.0`) — breaking change (settings format, hotkey schema, sync layout, etc.)

### When the user says "release", "publish", "new version", "deel met gebruikers", or similar:

Do **not** auto-pick a version. Always ask first:

> "Is dit een PATCH (bugfix), MINOR (nieuwe feature) of MAJOR (breaking change)? Huidige versie is `<latest tag>`."

Get the latest tag with `git tag --list --sort=-v:refname | head -1`.

### Release procedure (after user confirms version bump)

Run these in order. Replace `vX.Y.Z` with the agreed version.

1. **Verify clean state** — `git status` should be clean, or stage only intentional changes.
2. **Update `CHANGELOG.md`**:
   - Move everything under `## [Unreleased]` into a new `## [X.Y.Z] — YYYY-MM-DD` section.
   - Leave `## [Unreleased]` empty (or with subsection headers) at the top for next iteration.
   - Update the comparison links at the bottom: change `[Unreleased]` to compare from the new tag, and add a `[X.Y.Z]` link.
3. **Commit** any pending work with a conventional message (`feat:`, `fix:`, `docs:`, `refactor:`, `chore:`). Include the changelog update in this commit.
4. **Tag** the commit:
   ```
   git tag -a vX.Y.Z -m "vX.Y.Z - <one-line summary>"
   ```
5. **Build the publish artifact** (self-contained, no .NET runtime needed on target machine):
   ```
   rm -rf publish/SnippetLauncher-win-x64
   dotnet publish src/SnippetLauncher.App/SnippetLauncher.App.csproj \
     -c Release -r win-x64 --self-contained true \
     -o publish/SnippetLauncher-win-x64
   ```
6. **Zip** via PowerShell (matches existing naming):
   ```
   Compress-Archive -Path publish\SnippetLauncher-win-x64 \
     -DestinationPath publish\SnippetLauncher-vX.Y.Z-win-x64.zip -Force
   ```
7. **Push** the commit and tag to GitHub:
   ```
   git push origin master
   git push origin vX.Y.Z
   ```
8. **Create GitHub Release** with the zip attached. Use the new changelog section as release notes (extract the `## [X.Y.Z]` block from `CHANGELOG.md`):
   ```
   gh release create vX.Y.Z publish/SnippetLauncher-vX.Y.Z-win-x64.zip \
     --title "vX.Y.Z - <one-line summary>" \
     --notes "<paste the [X.Y.Z] section content from CHANGELOG.md>"
   ```

### Naming conventions

- Tag format: `v1.0.0` (lowercase `v` prefix)
- Zip filename: `SnippetLauncher-v<version>-win-x64.zip` in `publish/`
- Commit subjects: imperative, prefixed (`feat:`, `fix:`, …)

## Build / Test

- Solution: `Snippets.sln`
- Target framework: .NET 10 (WPF, Windows-only)
- Run tests: `dotnet test`
- Run app from source: `dotnet run --project src/SnippetLauncher.App`

## Repo layout

- `src/SnippetLauncher.Core/` — domain, storage, sync, placeholders (no UI deps)
- `src/SnippetLauncher.App/` — WPF UI (Views, ViewModels, Services)
- `tests/SnippetLauncher.Core.Tests/` — xUnit tests
- `snippets/` — **user content, gitignored** (each user has their own)
- `publish/` — **build output, gitignored** (release zips live here locally)
- `docs/plans/` — design plans
- `docs/setup-second-user.md` — onboarding guide for additional users
