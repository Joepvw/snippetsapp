# Architecture overview

High-level map of how SnippetLauncher fits together. Read this before diving into a specific layer. Per-feature design lives in [docs/plans/](plans/); per-incident learnings in [docs/solutions/](solutions/); operational fix-it procedures in [docs/runbooks/](runbooks/).

## System diagram

```
                       ┌──────────────────────────────────────────────┐
                       │  Windows OS                                   │
                       │  ┌────────────────────────────────────────┐  │
                       │  │  RegisterHotKey (Win32)                │  │
                       │  └─────────┬──────────────────────────────┘  │
                       └────────────┼─────────────────────────────────┘
                                    │ WM_HOTKEY
                                    ▼
   SnippetLauncher.App  ┌──────────────────────────────────────────────┐
   (WPF, Windows-only)  │  GlobalHotkeyService  ── shows ─▶  Search    │
                        │                                    Popup     │
                        │  ┌──────────────────────────────────────┐    │
                        │  │  Views ◀── bind ── ViewModels        │    │
                        │  │  (XAML)             (MVVM, Toolkit)  │    │
                        │  └──────────────┬───────────────────────┘    │
                        │  ┌──────────────▼───────────────────────┐    │
                        │  │  Services (WPF impls)                │    │
                        │  │  WpfClipboard, WpfDialog,            │    │
                        │  │  WindowsStartup, GlobalHotkey        │    │
                        │  └──────────────┬───────────────────────┘    │
                        └─────────────────┼────────────────────────────┘
                                          │  via DI seams (interfaces)
                                          ▼
   SnippetLauncher.Core  ┌─────────────────────────────────────────────┐
   (no UI deps,          │   Abstractions: IClock, IClipboardService,  │
    fully testable)      │   IDialogService, ICommandBus, IGitService, │
                         │   IGlobalHotkeyService                       │
                         │                                              │
                         │   Search ──▶ SnippetRepository ──▶ Storage  │
                         │     │              │                         │
                         │     │              ├──▶ Serializer (YAML)   │
                         │     │              └──▶ UsageStore          │
                         │     │                                        │
                         │   Placeholders (PlaceholderEngine)           │
                         │     │                                        │
                         │   Sync ──▶ GitService ──▶ LibGit2Sharp       │
                         │              │  (single dedicated worker    │
                         │              │   thread, Channel<Op> queue) │
                         │              └──▶ PushQueueStore (offline)  │
                         └─────────────────────────────────────────────┘
                                          │
                                          ▼
                         ┌─────────────────────────────────────────────┐
                         │  Filesystem                                  │
                         │  %APPDATA%/SnippetLauncher/                  │
                         │    settings.json   usage.json                │
                         │    push-queue.json   log/app.log             │
                         │  <snippets-dir>/  (user-chosen, git repo)    │
                         │    *.md   .git/   .local/conflicts/          │
                         └─────────────────────────────────────────────┘
```

## Layers

### `SnippetLauncher.Core` — pure domain & infrastructure

Headless, no `System.Windows.*`, fully xUnit-testable. Enforced by `tests/SnippetLauncher.App.Tests/ArchitectureTests.cs` (NetArchTest).

- **`Abstractions/`** — interfaces that act as DI seams. Anything Core needs from the OS or UI goes through one of these. Don't `new` concrete types in Core code; inject the interface.
- **`Domain/`** — `Snippet` (record), `Placeholder`, `SnippetUsage`. Immutable, serializable, no behavior.
- **`Storage/`** — `SnippetRepository` (single-writer via `Channel<Op>`), `SnippetSerializer` (YAML frontmatter + body), `UsageStore` (debounced flush of stats), `SlugHelper`. Real filesystem; no in-memory abstraction.
- **`Search/`** — `SearchService` builds an in-memory weighted index (60% title / 30% tags / 10% body) with recency + frequency boost. Recomputed on `SnippetChanged`.
- **`Placeholders/`** — `PlaceholderEngine` resolves `{date}`, `{time}`, `{clipboard}`, custom tokens. `{{` escapes a literal brace.
- **`Sync/`** — `GitService` runs **all** LibGit2Sharp calls on one dedicated worker thread fed by a `Channel`. `PushQueueStore` persists pending pushes to disk so offline restarts don't lose work. Last-writer-wins conflict resolution; backups land in `<snippets-dir>/.local/conflicts/`.
- **`Commands/`** + **`Infrastructure/`** — `ICommandBus` with in-process implementation. Commands like `OpenSearchCommand`, `QuickAddCommand` are how the UI tells Core to do things without VMs needing to know about each other.
- **`Settings/`** — `AppSettings`, `HotkeyBinding`, `SettingsService`. JSON-persisted at `%APPDATA%/SnippetLauncher/settings.json`.

### `SnippetLauncher.App` — WPF host

Composition root, UI, and platform-specific service implementations.

- **`App.xaml.cs`** — single-instance mutex (`SnippetLauncher_SingleInstance_Mutex`), named-pipe IPC (`SnippetLauncher_IPC`) so a second launch wakes the existing one, Serilog setup, full DI container assembly. This is where `IClipboardService → WpfClipboardService` etc. get wired.
- **`Views/`** — XAML windows. `SearchPopupWindow` is borderless and topmost; `EditorWindow` for CRUD; `PlaceholderFillDialog` when a snippet has unfilled tokens; `FirstRunWizardWindow` for onboarding; `SettingsWindow`.
- **`ViewModels/`** — MVVM with `CommunityToolkit.Mvvm` source generators. Bind to Core services via DI.
- **`Services/`** — `GlobalHotkeyService` (P/Invoke `RegisterHotKey` + the Win11 24H2 ALT trick — see [docs/solutions/2026-04-28-win11-24h2-hotkey-alt-trick.md](solutions/2026-04-28-win11-24h2-hotkey-alt-trick.md)), `WpfClipboardService`, `WpfDialogService`, `WindowsStartupService` (Run-key registration for startup-on-login).

### Tests

- **`SnippetLauncher.Core.Tests`** — xUnit + FluentAssertions. Integration tests use **real** temp filesystem and **real** temp git repos. This is intentional: storage and sync are the highest-risk surface and mocks have masked real bugs. Coverage gate: ≥70% line on Core (CI fails below).
- **`SnippetLauncher.App.Tests`** — NetArchTest only. Enforces "Core has no WPF refs" and similar invariants. No behavioral WPF tests; UI is verified manually.

## Data flow: typical search-and-paste

1. User presses global hotkey → `WM_HOTKEY` → `GlobalHotkeyService` raises event.
2. App applies the ALT-trick foreground workaround if needed, shows `SearchPopupWindow`.
3. User types → `SearchPopupViewModel` debounces 150 ms → calls `SearchService.Search(query)`.
4. User picks result + Enter → `PlaceholderEngine.Resolve(snippet)`. If unfilled tokens, show `PlaceholderFillDialog`.
5. Final text → `IClipboardService.SetText` → close popup → user pastes manually (Ctrl+V into the previously-focused app).
6. `UsageStore` records the pick (debounced flush to `usage.json`); `SearchService` index gets a recency/frequency bump.

## Data flow: edit and sync

1. Editor saves → `SnippetRepository.SaveAsync(snippet)` enqueues a write op on its `Channel`.
2. Single-writer worker writes the YAML file, raises `SnippetChanged`.
3. `GitService` watches changes → enqueues commit + push ops on **its** channel.
4. Git worker thread (dedicated) commits. Push attempts; on failure, op lands in `PushQueueStore` for retry next session.
5. On startup or network return: `PushQueueStore.Drain()` replays pending ops.

## Where things live at runtime

| Path | Contents |
|---|---|
| `%APPDATA%/SnippetLauncher/settings.json` | App settings (hotkey, snippets dir, sync prefs) |
| `%APPDATA%/SnippetLauncher/usage.json` | Per-snippet pick counts and last-used timestamps |
| `%APPDATA%/SnippetLauncher/push-queue.json` | Persisted pending git pushes (offline-safe) |
| `%APPDATA%/SnippetLauncher/log/app.log` | Serilog rolling daily, debug level |
| `<snippets-dir>/*.md` | User snippets (each = YAML frontmatter + markdown body) |
| `<snippets-dir>/.git/` | The sync git repo |
| `<snippets-dir>/.local/conflicts/` | Backups created when last-writer-wins resolves a conflict |

The `<snippets-dir>` defaults to `%APPDATA%/SnippetLauncher/snippets/` but most users set it to a user-chosen folder during the first-run wizard.

## Threading invariants (don't break)

- `GitService` runs **all** LibGit2Sharp calls on one dedicated thread. See [docs/solutions/2026-04-28-libgit2sharp-single-thread-worker.md](solutions/2026-04-28-libgit2sharp-single-thread-worker.md).
- `SnippetRepository` is a single-writer; mutations go through its `Channel`. Don't add a second writer.
- WPF `Dispatcher` is the only thread that touches Views. ViewModels marshal to it via `await` continuations on the captured context.

## What this doc is *not*

- Not a feature spec. Those live in [docs/plans/](plans/).
- Not a "why we made this decision" log. Those live in [docs/solutions/](solutions/).
- Not a fix-it guide. Those live in [docs/runbooks/](runbooks/).
- Not exhaustive — when you add a new top-level concept, update the diagram and the relevant layer paragraph.
