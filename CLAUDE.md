# SnippetLauncher — Project Notes for Claude

## Reading order for a fresh session

When you start working on this repo without prior context, read in this order — the whole stack is ~10 minutes:

1. **This file (CLAUDE.md)** — conventions, invariants, release procedure.
2. **[docs/architecture.md](docs/architecture.md)** — top-level system map. How Core, App, Sync, Storage, Search fit together.
3. **[docs/solutions/](docs/solutions/)** — institutional memory. Read when touching threading, hotkeys, or sync.
4. **[docs/runbooks/](docs/runbooks/)** — only when something is broken at runtime.
5. **[docs/plans/](docs/plans/)** — only when implementing or extending a planned feature.

Don't reverse-engineer architecture from code if `docs/architecture.md` answers the question.

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

The `release` skill (`.claude/skills/release/SKILL.md`) automates this — invoke it instead of running steps by hand. The steps below remain authoritative for reference.

Run these in order. Replace `vX.Y.Z` with the agreed version.

1. **Verify clean state** — `git status` should be clean, or stage only intentional changes.
2. **Bump version** in `Directory.Build.props` (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`, `<InformationalVersion>`) to `X.Y.Z` / `X.Y.Z.0`.
3. **Update `CHANGELOG.md`**:
   - Move everything under `## [Unreleased]` into a new `## [X.Y.Z] — YYYY-MM-DD` section.
   - Leave `## [Unreleased]` empty (or with subsection headers) at the top for next iteration.
   - Update the comparison links at the bottom: change `[Unreleased]` to compare from the new tag, and add a `[X.Y.Z]` link.
4. **Commit** any pending work with a conventional message (`feat:`, `fix:`, `docs:`, `refactor:`, `chore:`). Include the version bump and changelog update in this commit.
5. **Tag** the commit:
   ```
   git tag -a vX.Y.Z -m "vX.Y.Z - <one-line summary>"
   ```
6. **Build the publish artifact** (self-contained, no .NET runtime needed on target machine):
   ```
   rm -rf publish/SnippetLauncher-win-x64
   dotnet publish src/SnippetLauncher.App/SnippetLauncher.App.csproj \
     -c Release -r win-x64 --self-contained true \
     -o publish/SnippetLauncher-win-x64
   ```
7. **Zip** via PowerShell (matches existing naming):
   ```
   Compress-Archive -Path publish\SnippetLauncher-win-x64 \
     -DestinationPath publish\SnippetLauncher-vX.Y.Z-win-x64.zip -Force
   ```
8. **Push** the commit and tag to GitHub:
   ```
   git push origin master
   git push origin vX.Y.Z
   ```
9. **Create GitHub Release** with the zip attached. Use the new changelog section as release notes (extract the `## [X.Y.Z]` block from `CHANGELOG.md`):
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
- Target framework: .NET 10 (WPF, Windows-only — `net10.0-windows` TFM)
- Build: `dotnet build Snippets.sln -c Release` (~5–10 s clean, <3 s incremental)
- Run tests: `dotnet test` — Core suite ~67 tests, **8–12 s** is normal (integration tests spin up real temp git repos and real filesystem; do not "fix" this slowness with mocks, see test conventions below)
- Format check: `dotnet format Snippets.sln --verify-no-changes --severity warn` (CI gate; auto-fix locally with `dotnet format Snippets.sln`)
- Run app from source: `dotnet run --project src/SnippetLauncher.App`
- A second `dotnet run` will exit immediately (single-instance mutex `SnippetLauncher_SingleInstance_Mutex`); kill via `taskkill //F //IM SnippetLauncher.App.exe` if needed

## Repo layout

- `src/SnippetLauncher.Core/` — domain, storage, sync, placeholders (no UI deps)
- `src/SnippetLauncher.App/` — WPF UI (Views, ViewModels, Services)
- `tests/SnippetLauncher.Core.Tests/` — xUnit tests
- `tests/SnippetLauncher.App.Tests/` — NetArchTest boundary tests
- `snippets/` — **user content, gitignored** (each user has their own)
- `publish/` — **build output, gitignored** (release zips live here locally)
- `docs/architecture.md` — top-level system overview (read this first after CLAUDE.md)
- `docs/plans/` — design plans, one per feature (see `docs/plans/README.md`)
- `docs/solutions/` — institutional memory: solved problems with YAML frontmatter, searchable by tag
- `docs/runbooks/` — operational fix-it procedures for runtime failures (sync stuck, hotkey broken, snippet recovery)
- `docs/setup-second-user.md` — onboarding guide for additional users

## Architectural invariants (do not break)

These constraints are enforced by `tests/SnippetLauncher.App.Tests` (NetArchTest) and/or by convention. Touch them only with a deliberate plan.

- **Core has zero WPF / UI references.** `SnippetLauncher.Core` must remain headless and testable from xUnit. Anything that needs `System.Windows.*` belongs in `SnippetLauncher.App`. New cross-cutting concerns get an interface in Core (e.g. `IClipboardService`) with a WPF implementation in App.
- **GitService runs on a single dedicated worker thread.** LibGit2Sharp handles are not thread-safe. All git operations are queued through a `Channel`. Do not call `GitService` methods from arbitrary threads — submit ops to the channel.
- **SnippetRepository is single-writer.** Mutations go through a `Channel`; concurrent callers serialize naturally. Don't add a second writer path.
- **DI seams (`IClock`, `IClipboardService`, `IDialogService`, `ICommandBus`, `IGitService`, `IGlobalHotkeyService`) exist for testability.** Don't bypass them by `new`-ing concrete types in Core.

## Test conventions

- **Core.Tests uses real filesystem and real git repos in temp folders.** This is intentional — the storage and sync paths are the highest-risk surface and mocks have masked real bugs in the past. Don't "fix" these to use in-memory mocks without a strong reason.
- **App.Tests is for architectural enforcement** (NetArchTest), not behavioral WPF tests. UI behavior is verified manually via `dotnet run`.
- Coverage gate is **70% line-rate on Core**; CI fails below that. App layer is excluded from the gate.

## Working with this repo (compound engineering)

- **Plans first.** Non-trivial features get a plan in `docs/plans/YYYY-MM-DD-<slug>.md` before implementation. See `docs/plans/README.md` for the template.
- **Capture learnings.** When a non-obvious bug is fixed or a tricky architectural decision is made, add an entry to `docs/solutions/` so the next session (human or agent) can find it.
- **Parallel review on branch work.** Run `/review-branch` to spawn security, architecture, performance, and simplicity reviewers in parallel against the current branch diff before opening a PR.
