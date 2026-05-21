---
title: One-click installer + in-app update notification
type: feat
date: 2026-05-21
target_version: v1.1.0
supersedes: docs/plans/2026-04-29-feat-update-checker-plan.md
brainstorm: docs/brainstorms/2026-05-21-installer-onboarding-brainstorm.md
deepened: 2026-05-21
---

# One-click installer + in-app update notification

## Enhancement Summary

**Deepened on:** 2026-05-21
**Sections enhanced:** all (architecture, scope, all 4 phases, edge cases, risks, references)
**Review agents used:** security-sentinel, architecture-strategist, code-simplicity-reviewer, pattern-recognition-specialist, performance-oracle + 2× best-practices-researcher (Inno `[Code]`, .NET 10 timer/HTTP patterns, WPF toast architectuur)

### Key changes versus first draft
1. **Naming + DI shape**: `UpdateCheckCoordinator` → `UpdateNotificationService` (matcht `GlobalHotkeyService`-conventie). Event-based decoupling (`UpdateAvailable` event) ipv `TaskbarIcon` direct in constructor — matcht `gitSvc.StatusChanged`-patroon op `App.xaml.cs:235`.
2. **Async/timer modernisering**: `PeriodicTimer` (.NET 6+) ipv `System.Threading.Timer`. Lost silent-exception loss op én geeft nette `CancellationToken`-integratie via `IAsyncDisposable`.
3. **Aggressieve simplificatie** (matcht "klein scope" uit brainstorm): ETag/304-handling, "skip this version", "check now"-knop, en `installer-build-failure.md` runbook geschrapt. Settings krijgt **1** property (`UpdateCheckEnabled`) ipv 4. `UpdateCheckResult` is een **plain record** met nullable fields ipv discriminated union van 3 records.
4. **Toast wordt informatief** (geen klikhandler). Tray menu-item is enige klikbare actie. Elimineert H.NotifyIcon-versie-detectie-fragiliteit, één code-pad.
5. **Security hardening**: URL-validatie op `html_url` (HIGH — `Process.Start` + `UseShellExecute` is RCE-sink), URL zelf opbouwen uit gevalideerde `tag` als defense-in-depth, response-size limit (1 MB), `MaxAutomaticRedirections=3`, `SHA256SUMS.txt` als release-asset.
6. **DLL-planting mitigation**: `PrivilegesRequiredOverridesAllowed=dialog` zodat gevorderde gebruikers per-machine kunnen installeren in een NTFS-beschermde directory.
7. **Inno Setup robuustheid**: `[Code]`-sectie detecteert zip-installatie en vraagt eenmalig om bevestiging. `RestartApplications=yes`, `SolidCompression=yes`, `lzma2/ultra64` — kleinere setup.exe (~35-45 MB ipv ~80 MB).
8. **Tests flat in `Core.Tests/`**, niet in `Updates/`-submap (matcht bestaande conventie).

### Bewust afgewezen
- `IHttpClientFactory` (architecture-suggestie) — overkill voor één endpoint, één call per 24h. Simple singleton `HttpClient` met `SocketsHttpHandler { PooledConnectionLifetime = 15min }` is voldoende en matcht "geen overbodige abstractielagen"-stijl van de repo.
- Code-signing alsnog opnemen — kost geld, separate beslissing later. Mitigatie nu via `SHA256SUMS.txt` checksum.
- Pre-install backup van `settings.json` — `%APPDATA%\SnippetLauncher\` wordt door Inno nooit aangeraakt; backup voegt failure modes toe zonder reëel risico.

---

## Overview

Vervang de huidige zip-uitpak-flow door één `SnippetLauncher-Setup-vX.Y.Z.exe`
(Inno Setup, per-user install), en voeg een in-app update-check toe die via de
GitHub Releases API polt en bij een nieuwe versie een tray-toast laat zien plus
een persistent menu-item in het tray context-menu (de enige klikbare actie, die
de browser opent naar de release-pagina).

Dit plan **vervangt** [docs/plans/2026-04-29-feat-update-checker-plan.md](2026-04-29-feat-update-checker-plan.md)
— dat plan dekte alleen de Settings-button variant van de updater. Dit nieuwe
plan bundelt installer + updater omdat beide samen pas écht het "tweede gebruiker
meekrijgen"-probleem oplossen.

## Problem statement

Zie [brainstorm](../brainstorms/2026-05-21-installer-onboarding-brainstorm.md)
voor de volledige uitwerking. Kort: een nieuwe gebruiker meekrijgen op
SnippetLauncher kost nu 6 handmatige stappen (zip downloaden, uitpakken naar een
verstopt pad, .exe vinden, SmartScreen-blokkering opheffen, shortcut maken, app
starten). Voor niet-tech publiek is dit een dealbreaker. Updates lopen identiek —
gebruikers vergeten te updaten omdat er geen signaal in de app zit.

## Scope

### In scope
- Inno Setup `.iss`-config voor één per-user `setup.exe` (default per-user, optioneel per-machine via dialog)
- `installer/` map in de repo met de `.iss` onder version control
- Uitbreiding van `.claude/skills/release/SKILL.md`: `iscc.exe` compilatie + SHA256SUMS generatie + 3 release-assets (zip, setup.exe, SHA256SUMS.txt)
- Nieuwe Core-service `IUpdateCheckService` met GitHub Releases API call, semver-vergelijking, URL-validatie, response-size limit
- Periodieke check vanuit App (startup +30s na MainWindow.Show, daarna elke 24h via `PeriodicTimer`)
- Tray-toast bij nieuwe versie (informatief, niet klikbaar) via `H.NotifyIcon` `ShowNotification`
- Persistent tray menu-item "🆕 Update vX.Y.Z beschikbaar" zolang er een update klaarstaat — opent browser naar release-pagina
- Settings-checkbox "Automatisch controleren op updates (1× per dag)" gekoppeld aan `UpdateCheckEnabled`
- README-update + bijgewerkte `docs/setup-second-user.md` met installer-flow

### Out of scope (bewuste afbakening)
- **Git-authenticatie voor niet-tech publiek** — apart toekomstig plan (zie brainstorm "Te beslissen vóór planning")
- **Code-signing** — separate beslissing, blijft hobbel bij eerste install; SHA256-checksum is interim integriteits-anker
- **Silent auto-update** (Velopack/Squirrel-stijl) — kan als MINOR feature later
- **MSI / MSIX / winget / Microsoft Store** distributie
- **First-run wizard-uitbreiding** met update-instellingen
- **"Nu controleren"-knop in Settings** — geschrapt na simplicity-review; gebruiker herstart app voor handmatige check
- **"Deze versie overslaan"-feature** — geschrapt; auto-check uitzetten dekt de user-need
- **ETag/If-None-Match polling-optimalisatie** — 2 calls/dag is ruim onder 60/uur anonymous rate-limit
- **In-app download van setup.exe** — klik opent browser, gebruiker doet de rest

## Architecture

### New module: `SnippetLauncher.Core/Updates/`

```
SnippetLauncher.Core/Updates/
  IUpdateCheckService.cs       // interface, neemt CancellationToken
  GitHubUpdateCheckService.cs  // GitHub Releases API impl (Core, geen WPF)
  UpdateCheckResult.cs         // single record met nullable fields
  ReleaseInfo.cs               // POCO: Tag, Url, PublishedAt
```

Core blijft WPF-vrij (enforced by NetArchTest in `tests/SnippetLauncher.App.Tests`).
`HttpClient` wordt constructor-geïnjecteerd; tests gebruiken `FakeHttpMessageHandler`.

```csharp
public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken ct);
}

public sealed record UpdateCheckResult(
    Version? NewVersion,
    string? ReleaseUrl,
    string? FailureReason);
```

Patroon-keuze: **plain record met nullable fields** (caller checkt `result.NewVersion is not null`). Geen discriminated union — voor 3 mutually-exclusive cases is `is not null` even duidelijk en sluit beter aan bij bestaande resultaat-patronen in de codebase (`GitSyncStatus` enum, plain records).

### New module: `SnippetLauncher.App/Services/`

```
SnippetLauncher.App/Services/
  UpdateNotificationService.cs  // PeriodicTimer + event raise
```

```csharp
public sealed class UpdateNotificationService : IAsyncDisposable
{
    public event Action<UpdateCheckResult>? UpdateAvailable;
    public Task StartAsync() { ... }   // 30s delay, daarna PeriodicTimer(24h)
    public ValueTask DisposeAsync() { ... }
}
```

`App.xaml.cs:BuildTrayIcon()` subscribet op `UpdateAvailable`, toont toast en
voegt/verwijdert het menu-item. Dit matcht het bestaande event-patroon
(`gitSvc.StatusChanged += ...` op `App.xaml.cs:235`). Klik-handlers blijven waar
álle andere tray menu-clicks wonen (`App.xaml.cs:319-345`).

### Settings additions (`SnippetLauncher.Core/Settings/AppSettings.cs`)

```csharp
public bool UpdateCheckEnabled { get; set; } = true;
```

**Eén** property. Geen `LastCheckAt`, geen `SkippedVersion`, geen `LastSeenETag`.
Bestaand `SettingsService.Load()` deserializer-patroon vult automatisch de default
voor nieuwe gebruikers — geen migratie nodig.

### Inno Setup (`installer/SnippetLauncher.iss`)

Self-contained `.iss` met:
- `AppId` GUID — **stabiel over alle releases** (eenmalig genereren, NOOIT wijzigen, commit in repo)
- `PrivilegesRequired=lowest` + `PrivilegesRequiredOverridesAllowed=dialog` (gevorderde gebruikers kunnen per-machine kiezen → NTFS-bescherming tegen DLL-planting in `{app}`)
- `DefaultDirName={localappdata}\Programs\SnippetLauncher`
- `DisableDirPage=yes`, `DisableProgramGroupPage=yes`
- `CloseApplications=force` + `RestartApplications=yes` (Restart Manager sluit draaiende app, start hem na install weer)
- `UsePreviousAppDir=yes` — update gebruikt vorige install-locatie
- `Compression=lzma2/ultra64` + `SolidCompression=yes` — kleinere setup.exe (~35-45 MB ipv ~80 MB) door cross-file dedup in self-contained .NET payload
- `OutputDir=publish`, `OutputBaseFilename=SnippetLauncher-Setup-v{#AppVersion}`
- `[Files]` source: `publish\SnippetLauncher-win-x64\*` met `ignoreversion recursesubdirs createallsubdirs`
- `[Icons]`: `{userprograms}\SnippetLauncher` altijd, `{userdesktop}\SnippetLauncher` via optionele `[Tasks]` checkbox (default uncheck)
- `[Run]`: `postinstall nowait skipifsilent` — "Start SnippetLauncher" checkbox
- `[Code]` `InitializeSetup()`: detecteert bestaande zip-installatie (map bestaat maar geen Inno uninstall-regkey) en vraagt eenmalig om bevestiging:

```pascal
[Code]
function IsInnoManagedInstall(): Boolean;
begin
  Result := RegKeyExists(HKEY_CURRENT_USER,
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1');
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if DirExists(ExpandConstant('{localappdata}\Programs\SnippetLauncher'))
     and not IsInnoManagedInstall() then
  begin
    if MsgBox(
      'Een bestaande SnippetLauncher-installatie is gevonden.' + #13#10 +
      'Deze wordt vervangen door de installer-versie. ' +
      'Je snippets en instellingen blijven behouden.' + #13#10#13#10 +
      'Doorgaan?',
      mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;
```

Versie als parameter: `iscc /DAppVersion=1.1.0 installer\SnippetLauncher.iss`.

## Implementation phases

### Phase 1 — Inno Setup installer + zip→installer migratie-prompt

Aanmaken: `installer/SnippetLauncher.iss` met config + `[Code]`-sectie hierboven.
`installer/README.md` met one-liner "Install Inno Setup 6 van https://jrsoftware.org/isinfo.php".

Update `docs/setup-second-user.md` Stap 1: vervang zip-uitpakproces door "download `SnippetLauncher-Setup-vX.Y.Z.exe`, dubbelklik, doorklik". SmartScreen "Toch uitvoeren"-instructie hier opnemen (één zin + screenshot in release notes).

Files affected:
- **new** `installer/SnippetLauncher.iss`
- **new** `installer/README.md`
- **edit** `docs/setup-second-user.md`

Verificatie:
- Lokale build via `iscc /DAppVersion=1.0.4 installer\SnippetLauncher.iss` produceert `publish\SnippetLauncher-Setup-v1.0.4.exe`
- Installer op schone Windows 11 VM: per-user install zonder UAC, Start Menu shortcut werkt
- Installer over bestaande zip-installatie: prompt verschijnt, na ja: installatie slaagt, data ongemoeid
- Tweede installer-run (echte update-scenario): geen prompt meer, installeert silent over vorige Inno-install

Acceptance:
- [ ] `.iss` compileert zonder warnings
- [ ] Per-user install (geen UAC-prompt) in `%LOCALAPPDATA%\Programs\SnippetLauncher\`
- [ ] Optie voor per-machine via `PrivilegesRequiredOverridesAllowed=dialog`
- [ ] Setup.exe < 50 MB (lzma2/ultra64 + solid)
- [ ] Uninstaller in Apps & Features (user-scope), verwijdert alleen `{app}`
- [ ] Zip→installer migratie-prompt verschijnt eenmalig, niet bij echte updates
- [ ] AppId GUID staat in source control met comment "DO NOT CHANGE"

### Phase 2 — Release pipeline + SHA256 checksums

Uitbreiden: `.claude/skills/release/SKILL.md` (en sectie "Release procedure" in CLAUDE.md).

Nieuwe stappen tussen huidige stap 7 (zip) en 8 (push):

```powershell
# Stap 7b — Inno Setup compileren
$Iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $Iscc)) {
  throw "Inno Setup 6 niet gevonden op $Iscc. Install van https://jrsoftware.org/isinfo.php"
}
& $Iscc "/DAppVersion=$Version" "installer\SnippetLauncher.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

# Stap 7c — SHA256 checksums
$Assets = @(
  "publish\SnippetLauncher-v$Version-win-x64.zip",
  "publish\SnippetLauncher-Setup-v$Version.exe"
)
Get-FileHash $Assets -Algorithm SHA256 |
  ForEach-Object { "$($_.Hash.ToLower())  $(Split-Path $_.Path -Leaf)" } |
  Set-Content -Encoding ascii publish\SHA256SUMS.txt
```

Stap 9 (`gh release create`) krijgt 3 assets en SHA's in de notes:

```powershell
$Sha = Get-Content publish\SHA256SUMS.txt -Raw
gh release create v$Version `
  "publish\SnippetLauncher-v$Version-win-x64.zip" `
  "publish\SnippetLauncher-Setup-v$Version.exe" `
  "publish\SHA256SUMS.txt" `
  --title "v$Version - <one-line summary>" `
  --notes "<changelog block>`n`n## SHA256 checksums`n``````n$Sha```````"
```

Files affected:
- **edit** `.claude/skills/release/SKILL.md`
- **edit** `CLAUDE.md` — sectie "Release procedure" bijwerken

Acceptance:
- [ ] Skill faalt vroeg en duidelijk als `iscc.exe` ontbreekt (geen halve release)
- [ ] Drie artefacten op de GitHub Release-pagina: zip, setup.exe, SHA256SUMS.txt
- [ ] Release notes bevatten checksum-blok
- [ ] Skill idempotent — opnieuw draaien na een halve fail werkt

### Phase 3 — `IUpdateCheckService` (Core) + security hardening

Implementatie volgt simple-record pattern met expliciete URL-validatie:

```csharp
// GitHubUpdateCheckService.CheckAsync (kerngedeelte)
const long MaxResponseBytes = 1_000_000; // 1 MB

using var req = new HttpRequestMessage(HttpMethod.Get,
    "https://api.github.com/repos/Joepvw/snippetsapp/releases/latest");
req.Headers.UserAgent.ParseAdd($"SnippetLauncher/{currentVersion} (+https://github.com/Joepvw/snippetsapp)");
req.Headers.Accept.ParseAdd("application/vnd.github+json");
req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
    .ConfigureAwait(false);

if (resp.StatusCode == HttpStatusCode.NotFound)
    return new UpdateCheckResult(null, null, "No releases found");
if ((int)resp.StatusCode >= 400)
    return new UpdateCheckResult(null, null, $"HTTP {(int)resp.StatusCode}");
if (resp.Content.Headers.ContentLength is { } len && len > MaxResponseBytes)
    return new UpdateCheckResult(null, null, "Response too large");

await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
var dto = await JsonSerializer.DeserializeAsync<ReleaseDto>(stream, _jsonOpts, ct)
    .ConfigureAwait(false);

var tag = (dto?.TagName ?? "").TrimStart('v');
if (!System.Version.TryParse(tag, out var newVersion))
    return new UpdateCheckResult(null, null, "Unparseable version");

if (newVersion <= currentVersion)
    return new UpdateCheckResult(null, null, null);

// Bouw URL zelf op uit gevalideerde tag — vertrouw html_url NIET voor ShellExecute
var safeUrl = $"https://github.com/Joepvw/snippetsapp/releases/tag/v{newVersion}";
return new UpdateCheckResult(newVersion, safeUrl, null);
```

DI-registratie (in `App.xaml.cs:ConfigureServices`):

```csharp
services.AddSingleton(_ => new HttpClient(new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(15),
    MaxAutomaticRedirections = 3,
})
{
    Timeout = TimeSpan.FromSeconds(10),
});
services.AddSingleton<IUpdateCheckService, GitHubUpdateCheckService>();
```

Files affected:
- **new** `src/SnippetLauncher.Core/Updates/IUpdateCheckService.cs`
- **new** `src/SnippetLauncher.Core/Updates/GitHubUpdateCheckService.cs`
- **new** `src/SnippetLauncher.Core/Updates/UpdateCheckResult.cs`
- **new** `src/SnippetLauncher.Core/Updates/ReleaseDto.cs` (internal POCO voor JSON-deserialisatie)

Tests **flat** in `tests/SnippetLauncher.Core.Tests/` (matcht bestaande conventie):
- **new** `GitHubUpdateCheckServiceTests.cs` — `FakeHttpMessageHandler` met geprepareerde responses:
  - 200 met newer version → `UpdateAvailable`
  - 200 met same/older version → no-update
  - 200 met malformed JSON → `FailureReason` gezet
  - 200 met oversized body (`Content-Length > 1MB`) → "Response too large"
  - 404 → "No releases found"
  - 403/429 → "HTTP 403/429"
  - Network exception → `FailureReason` gezet
  - **Security**: malicious `html_url` payloads (`file://`, `\\server\share`, `javascript:`, `ms-msdt:`, `https://github.com.evil.com/`, `https://github.com@evil.com/`) — verifieer dat `ReleaseUrl` altijd `https://github.com/Joepvw/snippetsapp/releases/tag/v{version}` is (zelf opgebouwd, niet `html_url`)
- **new** `VersionComparisonTests.cs` — `1.0.10` > `1.0.9`, `v1.0.0` vs `1.0.0`, edge cases
- **new** `FakeHttpMessageHandler.cs` — naast bestaande `FakeClock.cs`, zelfde stijl (`internal sealed class`)

Coverage target: 100% op deze module.

Acceptance:
- [ ] Service testbaar zonder netwerk via injected `HttpMessageHandler`
- [ ] Alle 8 response-scenario's + 6 malicious-URL scenario's gedekt
- [ ] `ReleaseUrl` wordt altijd zelf opgebouwd uit gevalideerde tag — `html_url` uit API wordt nooit doorgegeven aan `Process.Start`
- [ ] Response-stream is begrensd op 1 MB
- [ ] Alle `await`s in Core gebruiken `ConfigureAwait(false)`
- [ ] `CancellationToken` doorgegeven tot in `HttpClient.SendAsync` en `JsonSerializer.DeserializeAsync`
- [ ] Geen WPF-deps (NetArchTest in App.Tests blijft groen)

### Phase 4 — `UpdateNotificationService` + tray-toast + Settings checkbox

`UpdateNotificationService` (App-side, owns timer en event):

```csharp
public sealed class UpdateNotificationService : IAsyncDisposable
{
    private readonly IUpdateCheckService _check;
    private readonly ISettingsService _settings;
    private readonly Version _currentVersion;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public event Action<UpdateCheckResult>? UpdateAvailable;

    public void Start()
    {
        _loop = RunLoopAsync(_cts.Token);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
            do
            {
                await TickAsync(ct).ConfigureAwait(false);
            } while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex) { /* log via existing logger, don't crash */ }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        if (!_settings.Load().UpdateCheckEnabled) return;
        var result = await _check.CheckAsync(_currentVersion, ct).ConfigureAwait(false);
        if (result.NewVersion is not null)
            UpdateAvailable?.Invoke(result);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_loop is not null) { try { await _loop.ConfigureAwait(false); } catch { } }
        _cts.Dispose();
    }
}
```

`Start()` wordt aangeroepen vanuit `MainWindow.Loaded` of via `Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, ...)` — houdt eerste paint schoon (perf-reviewer aanbeveling).

`BuildTrayIcon` in `App.xaml.cs` subscribet op het event en marshalt naar Dispatcher:

```csharp
updateSvc.UpdateAvailable += result => Dispatcher.BeginInvoke(() => {
    _pendingUpdate = result;
    _trayIcon.ShowNotification(
        title: "Update beschikbaar",
        message: $"SnippetLauncher v{result.NewVersion} is uit.",
        icon: NotificationIcon.Info);
    RebuildTrayMenu(); // voegt "🆕 Update v{version} beschikbaar"-item bovenaan toe
});
```

Toast is **informatief** (geen klik-handler — elimineert H.NotifyIcon versie-detectie-fragiliteit). Het menu-item is de enige klikbare actie:

```csharp
var item = new MenuItem { Header = $"🆕 Update v{_pendingUpdate.NewVersion} beschikbaar" };
item.Click += (_, _) => Process.Start(new ProcessStartInfo(_pendingUpdate.ReleaseUrl) {
    UseShellExecute = true
});
```

`ReleaseUrl` is hier al gevalideerd én zelf-opgebouwd door `GitHubUpdateCheckService` (Phase 3) — `UseShellExecute` is veilig.

Settings UI (`SettingsWindow.xaml` / `SettingsViewModel`):
- **Eén** checkbox: "Automatisch controleren op updates (1× per dag)" gebonden aan `UpdateCheckEnabled`
- Geen status-tekst, geen "Nu controleren"-knop, geen "Deze versie overslaan"-knop

Files affected:
- **new** `src/SnippetLauncher.App/Services/UpdateNotificationService.cs`
- **edit** `src/SnippetLauncher.App/App.xaml.cs` — DI-registratie, Start in MainWindow.Loaded, Dispose in OnExit, event-subscriber in BuildTrayIcon
- **edit** `src/SnippetLauncher.App/Views/SettingsWindow.xaml` — één checkbox toevoegen
- **edit** `src/SnippetLauncher.App/ViewModels/SettingsViewModel.cs` — `UpdateCheckEnabled` binding
- **edit** `src/SnippetLauncher.Core/Settings/AppSettings.cs` — 1 nieuwe property

Tests:
- App-services worden niet uitvoerig getest (NetArchTest-conventie). Coordinator is dunne plumbing — geen aparte tests, gedrag wordt gedekt door `IUpdateCheckService`-tests in Core
- Wel: 1 smoke-test voor `UpdateNotificationService` dat event fires bij `NewVersion != null` en niet bij `UpdateCheckEnabled = false`

Acceptance:
- [ ] Auto-check werkt 30s na MainWindow.Show, daarna elke 24h
- [ ] Respecteert `UpdateCheckEnabled`
- [ ] Toast verschijnt bij nieuwe versie
- [ ] Menu-item "🆕 Update vX.Y.Z beschikbaar" verschijnt en blijft staan tot herstart of nieuwere versie
- [ ] Klik op menu-item opent juiste release-pagina in browser
- [ ] `Process.Start` krijgt nooit een externe URL — alleen zelf opgebouwde `github.com/Joepvw/snippetsapp/releases/tag/...`
- [ ] Settings-checkbox werkt, persisteert in `settings.json`
- [ ] Shutdown annuleert in-flight HTTP-call netjes via `CancellationToken`
- [ ] `UpdateNotificationService` is `IAsyncDisposable`, wordt gedispose'd in `OnExit`

## Edge cases & user flows

### Flow A — Fresh install voor nieuwe gebruiker
1. Gebruiker klikt `SnippetLauncher-Setup-v1.1.0.exe` op GitHub Releases
2. SmartScreen waarschuwt → instructie in release notes: "Meer informatie → Toch uitvoeren"
3. Installer draait per-user (geen UAC), 3 schermen: Welkom → Optionele desktopicon → Klaar (met "Start app"-checkbox)
4. App start → first-run wizard zoals nu

### Flow B — Update via in-app discovery (happy path)
1. App polt 30s na MainWindow.Show, vindt nieuwere versie
2. Tray-toast verschijnt: "Update beschikbaar — SnippetLauncher v1.1.0 is uit."
3. Tray menu krijgt een nieuw item bovenaan: "🆕 Update v1.1.0 beschikbaar"
4. Gebruiker klikt menu-item → browser opent `https://github.com/Joepvw/snippetsapp/releases/tag/v1.1.0`
5. Gebruiker download nieuwe `setup.exe`, draait die
6. Inno Setup detecteert oude Inno-install (zelfde AppId), sluit draaiende app via Restart Manager, installeert over heen, herstart app

### Flow C — Bestaande zip-gebruiker upgradet naar installer (eenmalig)
1. Gebruiker zit op zip-uitpak in `%LOCALAPPDATA%\Programs\SnippetLauncher\`
2. Gebruiker download eerste installer-versie en draait die
3. `InitializeSetup`-prompt: "Bestaande SnippetLauncher-installatie gevonden, wordt vervangen. Doorgaan?"
4. Ja → Restart Manager sluit app, Inno overschrijft `{app}` (data in `%APPDATA%` ongemoeid), Inno-uninstall-regkey wordt geregistreerd
5. Volgende update: geen prompt meer, gewoon zoals Flow B

### Flow D — Geen internet / API rate limit / netwerkfout
- `CheckAsync` retourneert `UpdateCheckResult(null, null, "...")` — geen event fired
- Geen toast, geen menu-item, geen user-disruption
- Volgende tick (24h later of bij herstart) probeert opnieuw

### Flow E — Update beschikbaar maar gebruiker negeert
- Toast verdwijnt na ~5s
- Menu-item blijft staan zolang app draait — robuust signaal
- Bij herstart: nieuwe check, menu-item verschijnt opnieuw
- Gebruiker wil definitief stoppen met meldingen: Settings → uncheck "Automatisch controleren"

### Flow F — Update-check fires tijdens git-sync
- Onafhankelijke threads (PeriodicTimer-loop ↔ GitService worker-thread), geen interactie
- Beide tonen non-modale UI (toast/menu-item ↔ sync-status), geen stacking-issue

### Flow G — App afgesloten als 24h-tick zou vuren
- Tick gemist (App niet actief, dat is OK)
- Bij volgende startup: na 30s draait de initial check sowieso
- Géén `LastCheckAt`-property nodig — meerdere checks per dag bij vaak herstarten is geen probleem (60/uur rate limit is ruim)

### Flow H — Shutdown midden in HTTP-call
- `OnExit` triggert `UpdateNotificationService.DisposeAsync`
- `CancellationTokenSource.Cancel()` propageert naar `HttpClient.SendAsync` → `OperationCanceledException`
- Loop vangt netjes af, geen post-disposal access exceptions

## Risks & mitigation

| Risico | Severity | Mitigatie |
|---|---|---|
| **`Process.Start` met externe URL als RCE-sink** | High | URL nooit uit `html_url` overnemen; zelf opbouwen uit gevalideerde `tag`. Hostname-whitelist `github.com`. Tests met malicious payloads (`file://`, `\\server\share`, `ms-msdt:`, etc.) |
| **DLL-planting in user-writeable `{app}`** | Medium | `PrivilegesRequiredOverridesAllowed=dialog` zodat gevorderde users per-machine kunnen installeren. SHA256SUMS.txt als release-asset voor integriteits-verificatie. Documenteren in solution-doc als bewuste trade-off voor user-scope install. |
| **AppId GUID per ongeluk gewijzigd in een PR** | High | Comment in `.iss` met "DO NOT CHANGE"; toevoegen aan PR-template-checklist |
| **SmartScreen blokkeert installer** | Medium | Duidelijke "Meer informatie → Toch uitvoeren"-instructie in release notes; SHA256-hash zichtbaar; long-term: code-signing (apart plan) |
| **Oversized HTTP-response (DoS-eigen-proces)** | Medium | `Content-Length`-check + 1 MB cap, `MaxAutomaticRedirections=3`, `Timeout=10s`, `ResponseHeadersRead` |
| **`iscc.exe` niet aanwezig op build-machine** | Medium | Vroege check in release-skill met duidelijke foutmelding (path + download-URL). Geen apart runbook. |
| **`CloseApplications=force` killt sync midden in commit** | Medium | Restart Manager stuurt `WM_QUERYENDSESSION` eerst; bestaande app-shutdown drained de git-channel netjes (verifiëren tijdens Phase 1 testing) |
| **Zip→installer transitie laat orphan files achter** | Low | `ignoreversion recursesubdirs` overschrijft alles in `{app}`; `[Code]` prompt waarschuwt user vooraf |
| **Anon rate limit getroffen** | Low | 2 calls/dag per user, 60/uur limit. Stille fail, retry op volgende tick |

## Open questions (post-plan, kunnen tijdens implementatie)

- **AppId GUID** — eenmaal genereren tijdens Phase 1 start. Voorstel: genereren via PowerShell `[guid]::NewGuid()`, opnemen in `installer/SnippetLauncher.iss` met comment "// AppId: DO NOT CHANGE — wijzigen breekt updates voor alle bestaande gebruikers"
- **`MainWindow.Loaded` vs `Dispatcher.BeginInvoke(ApplicationIdle)`** voor `UpdateNotificationService.Start()` — beide zijn valide; kies wat past bij bestaande `MainWindow.Loaded`-event handlers
- **Hoe de UpdateAvailable-toast/menu-item de tray UI consistent updaten** als de check fires terwijl gebruiker het context-menu open heeft — wsl. geen probleem, valideren tijdens Phase 4 testing

## Acceptance criteria (overall)

- [ ] Een nieuwe gebruiker (niet-tech) krijgt SnippetLauncher in 3 klikken werkend: download → installer-doorklikken → app start
- [ ] Een bestaande installatie krijgt binnen 24h na een nieuwe release een tray-toast + persistent menu-item
- [ ] Klik op menu-item opent de juiste release-pagina in browser (zelf opgebouwde URL, niet API-verstrekt)
- [ ] Updaten = nieuwe setup.exe over de oude heen draaien; data en settings ongemoeid
- [ ] Setup.exe < 50 MB
- [ ] `dotnet test` blijft groen, coverage ≥ 70% op Core
- [ ] `dotnet format Snippets.sln --verify-no-changes` blijft groen
- [ ] Release-skill draait end-to-end en levert 3 artefacten + SHA256-blok in release notes

## References

### Internal
- Brainstorm: [docs/brainstorms/2026-05-21-installer-onboarding-brainstorm.md](../brainstorms/2026-05-21-installer-onboarding-brainstorm.md)
- Voorganger-plan (te superseden): [docs/plans/2026-04-29-feat-update-checker-plan.md](2026-04-29-feat-update-checker-plan.md)
- Architectuur: [docs/architecture.md](../architecture.md)
- Release-skill: [.claude/skills/release/SKILL.md](../../.claude/skills/release/SKILL.md)
- Versie-attribuut lezen: `src/SnippetLauncher.App/App.xaml.cs:28-32`
- DI-registratie patroon: `src/SnippetLauncher.App/App.xaml.cs:180-222`
- Tray-icoon: `src/SnippetLauncher.App/App.xaml.cs:307-352`
- Event-pattern voor App-side subscribers: `src/SnippetLauncher.App/App.xaml.cs:235` (`gitSvc.StatusChanged`)
- Disposal-volgorde in OnExit: `src/SnippetLauncher.App/App.xaml.cs:411-427`
- Settings-conventie: `src/SnippetLauncher.Core/Settings/AppSettings.cs`, `SettingsService.cs:8-12,32`
- DI-seam conventie: `src/SnippetLauncher.Core/Abstractions/`
- Test-stub patroon: `tests/SnippetLauncher.Core.Tests/FakeClock.cs`
- Single-instance mutex: `src/SnippetLauncher.App/App.xaml.cs:25`
- LibGit2Sharp threading learning: [docs/solutions/2026-04-28-libgit2sharp-single-thread-worker.md](../solutions/2026-04-28-libgit2sharp-single-thread-worker.md)
- Win11 24H2 hotkey learning (toast/foreground-race relevantie): [docs/solutions/2026-04-28-win11-24h2-hotkey-alt-trick.md](../solutions/2026-04-28-win11-24h2-hotkey-alt-trick.md)

### External — Inno Setup
- [Inno Setup PrivilegesRequired](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm)
- [Inno Setup CloseApplications + RestartApplications](https://jrsoftware.org/ishelp/topic_setup_closeapplications.htm)
- [Inno Setup preprocessor `/D`](https://jrsoftware.org/ishelp/topic_isppcc.htm)
- [Inno Setup DirExists (Code section)](https://jrsoftware.org/ishelp/topic_isxfunc_direxists.htm)
- [Inno Setup [Files] flags (ignoreversion)](https://jrsoftware.org/ishelp/topic_filessection.htm)

### External — GitHub API + .NET patterns
- [GitHub REST API rate limits](https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api)
- [GitHub REST API best practices](https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api)
- [GitHub API versions](https://docs.github.com/en/rest/about-the-rest-api/api-versions)
- [PeriodicTimer (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/api/system.threading.periodictimer)
- [The Right Way To Use HttpClient In .NET (Milan Jovanović)](https://www.milanjovanovic.tech/blog/the-right-way-to-use-httpclient-in-dotnet)
- [ConfigureAwait FAQ (Stephen Toub)](https://devblogs.microsoft.com/dotnet/configureawait-faq/)

### External — WPF tray/toast
- [H.NotifyIcon repository](https://github.com/HavenDV/H.NotifyIcon)
- [H.NotifyIcon Discussion #136 — notification click events](https://github.com/HavenDV/H.NotifyIcon/discussions/136)
