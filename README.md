# Snippet Launcher

Lokale Windows-applicatie (WPF / .NET 10) die via een globale hotkey een fuzzy-search popup opent over een persoonlijke snippet-bibliotheek. De gekozen snippet wordt naar het klembord gekopieerd; plak zelf met **Ctrl+V** in de actieve applicatie.

## Vereisten

- Windows 10 22H2 of Windows 11
- [Git for Windows](https://git-scm.com/download/win) (aanbevolen voor Git-sync en Credential Manager)

## Installatie

1. Download `SnippetLauncher-v1.0.0-win-x64.zip` uit [Releases](../../releases).
2. Pak de zip uit naar een map naar keuze, bijv. `C:\Tools\SnippetLauncher\`.
3. **SmartScreen-waarschuwing omzeilen** (ongesigneerde binary):
   - Klik met de rechtermuisknop op `SnippetLauncher.App.exe`.
   - Kies **Eigenschappen**.
   - Vink onderaan **Blokkering opheffen** aan en klik **OK**.
   - Dubbelklik daarna gewoon op het `.exe`-bestand.
4. Bij eerste start verschijnt de **First-run wizard** — volg de stappen om een snippet-map te kiezen en eventueel een remote te koppelen.

## Gebruik

| Actie | Standaard sneltoets |
|---|---|
| Zoek-popup openen | **Ctrl+Shift+Space** |
| Snippet toevoegen vanuit klembord | **Ctrl+Shift+N** |

- Typ in de popup om te zoeken (fuzzy).
- **Enter** kopieert de geselecteerde snippet en sluit de popup.
- **Esc** sluit de popup zonder actie.
- Pijltjes **↑/↓** om door resultaten te navigeren.

## Snippetformaat

Zie [docs/snippet-format.md](docs/snippet-format.md) voor het Markdown-frontmatter formaat als je snippets handmatig wilt bewerken.

## Tweede gebruiker instellen

Zie [docs/setup-second-user.md](docs/setup-second-user.md).

## Bouwen vanuit broncode

```powershell
# Vereist: .NET 10 SDK
dotnet build src/SnippetLauncher.App/SnippetLauncher.App.csproj -c Release

# Self-contained publish (win-x64)
dotnet publish src/SnippetLauncher.App/SnippetLauncher.App.csproj `
    -c Release --self-contained -r win-x64 `
    -o publish/SnippetLauncher-win-x64
```

## Licentie

Privégebruik — geen publieke licentie.
