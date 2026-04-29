# Plan: In-App Update Checker (v1.1)

**Status:** Backlog — not started
**Created:** 2026-04-29
**Target version:** v1.1.0

## Goal

Let the user check for new releases from inside the app instead of manually visiting the GitHub releases page.

## Scope (Option A — "Simple updater")

- "Check for updates" button in Settings window
- Compares current app version to latest GitHub Release tag at `Joepvw/snippetsapp`
- If newer version exists: shows release notes + button to open the GitHub release page in the browser
- If up to date: silent confirmation
- Optional: auto-check on startup (once per day, behind a setting, default off)

**Out of scope** (defer to later, possibly Option B / Velopack):
- Downloading the zip in-app
- Replacing files automatically
- Restarting the app post-update
- Delta updates / silent updates

## Implementation sketch

1. **Read current version** from assembly metadata (`Assembly.GetExecutingAssembly().GetName().Version` or `InformationalVersion`).
   - Add `<Version>` and `<InformationalVersion>` to `SnippetLauncher.App.csproj` so the running exe knows what version it is.
2. **New service** `IUpdateCheckService` in `SnippetLauncher.Core/Updates/`:
   - `Task<UpdateCheckResult> CheckAsync(CancellationToken)`
   - Calls `GET https://api.github.com/repos/Joepvw/snippetsapp/releases/latest` (no auth needed for public repos, 60 req/hour anonymous limit is plenty)
   - Parses `tag_name`, `html_url`, `body`, `published_at`
   - Compares tags using `System.Version` (strip leading `v`)
3. **WPF impl** in `SnippetLauncher.App/Services/` — uses `HttpClient` with a sensible User-Agent.
4. **Settings UI** — button + status label in `SettingsWindow.xaml` / `SettingsViewModel`.
5. **Tests** in `SnippetLauncher.Core.Tests/Updates/`:
   - Version comparison (newer / equal / older / pre-release)
   - Tag-name parsing edge cases (`v1.0.0`, `1.0.0`, `v1.0.0-beta`)
   - Mock the HTTP layer with a fake handler.

## Open questions

- Show a tray notification when a new version is found on auto-check, or only update the Settings status?
- How to handle pre-release tags (`v1.1.0-beta1`) — ignore by default?
- Add a "Don't remind me about this version" option?

## Effort estimate

~half a day end-to-end including tests.
