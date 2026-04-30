# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.2] — 2026-04-30

### Added
- Settings → **Opslag** tab toont en bewerkt nu de GitHub-remote URL (was alleen wizard-only). Wijzigen herbouwt automatisch de `GitService` zodat de nieuwe origin direct actief is.
- Knop **Nu synchroniseren** in Settings én tray triggert nu een directe pull *gevolgd door* push (was push-only). Eerste plek om te kijken als snippets niet verschijnen.
- Versienummer zichtbaar in de Settings-footer en in de tray-tooltip — gevuld vanuit `AssemblyInformationalVersionAttribute` (= `Directory.Build.props`).
- `IGitService.PullNowAsync()` voor handmatige fetch + merge.

### Changed
- Wizard-copy verduidelijkt: "Snippets-map kiezen" → "Lokale werkmap kiezen" met uitleg dat de map een lokale werkkopie is en GitHub de sync-backend.

## [1.0.1] — 2026-04-30

### Fixed
- First-run wizard now clones the configured remote into an empty target folder instead of `git init` + dangling origin (which left the snippet library empty for second users). `RemoteUrl` is also persisted in settings, and an existing init-only repo with a configured origin will now bootstrap from the remote's default branch on the next pull. Wizard documentation in `docs/setup-second-user.md` updated accordingly.

### Documentation
- Added `CLAUDE.md` with release procedure for future Claude Code sessions.
- Expanded `README.md` with feature list, placeholder syntax reference, sync model diagram, and roadmap.
- Updated `docs/setup-second-user.md` with correct repository URL and standardized install location (`%LOCALAPPDATA%\Programs\SnippetLauncher\`).
- Added `CHANGELOG.md` (this file).
- Added plan for in-app update checker — [docs/plans/2026-04-29-feat-update-checker-plan.md](docs/plans/2026-04-29-feat-update-checker-plan.md).

## [1.0.0] — 2026-04-29

First usable release.

### Added
- WPF UI: search popup, snippet editor, settings window, first-run wizard.
- Global hotkey service — search popup (default `Ctrl+Shift+Space`) and quick-add (default `Ctrl+Shift+N`).
- Fuzzy search across snippet titles and content (FuzzySharp).
- Placeholder engine with `{name}` syntax — built-ins `{date}`, `{time}`, `{clipboard}`; literal braces via `{{` / `}}`.
- Git-based synchronization (LibGit2Sharp) — auto-commit, auto-push, auto-pull every 60 seconds.
- Push queue with retry-on-failure for offline scenarios.
- Conflict handling — remote wins, local copy backed up to `<snippets-map>/.local/conflicts/`.
- Windows tray icon (H.NotifyIcon.Wpf) with right-click menu.
- Start-at-login integration via Windows registry.
- Dark theme.
- Settings persistence in `%APPDATA%\SnippetLauncher\settings.json`.
- Logging to `%APPDATA%\SnippetLauncher\log\`.
- Self-contained Windows x64 publish — no .NET runtime required on target machines.

### Project infrastructure
- Solution split into `SnippetLauncher.Core` (domain, no UI dependencies) and `SnippetLauncher.App` (WPF UI).
- xUnit test suite covering placeholder engine, slug helper, hotkey binding, in-process command bus, and Git service.
- GitHub Actions CI workflow.

[Unreleased]: https://github.com/Joepvw/snippetsapp/compare/v1.0.2...HEAD
[1.0.2]: https://github.com/Joepvw/snippetsapp/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/Joepvw/snippetsapp/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/Joepvw/snippetsapp/releases/tag/v1.0.0
