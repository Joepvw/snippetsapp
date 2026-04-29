# Snippet Launcher

Lokale Windows-applicatie (WPF / .NET 10) die via een globale hotkey een fuzzy-search popup opent over een persoonlijke snippet-bibliotheek. De gekozen snippet wordt naar het klembord gekopieerd; plak zelf met **Ctrl+V** in de actieve applicatie.

## Features

- 🔍 **Fuzzy search popup** via globale hotkey, werkt vanuit elke applicatie
- ✏️ **Quick-add** vanaf klembord — snippet maken zonder de zoek-popup te openen
- 🧩 **Placeholders** — dynamische velden zoals `{naam}`, `{date}`, `{clipboard}`
- 🔄 **Git-sync** — deel je snippets-bibliotheek tussen machines via een private GitHub-repo
- 🖥️ **Tray-icoon** — app draait stilletjes op de achtergrond, instellingen via rechtsklik
- 🚀 **Start bij login** — optioneel, beheerd vanuit Instellingen
- 🌓 **Donker thema**

## Vereisten

- Windows 10 22H2 of Windows 11 (x64)
- [Git for Windows](https://git-scm.com/download/win) — vereist voor sync en Git Credential Manager

## Installatie

1. Download `SnippetLauncher-v1.0.0-win-x64.zip` uit [Releases](../../releases).
2. Pak de zip uit naar `%LOCALAPPDATA%\Programs\SnippetLauncher\`
   (typ dat pad in de adresbalk van Verkenner — Windows maakt de mappen automatisch aan).
3. **SmartScreen-waarschuwing omzeilen** (ongesigneerde binary):
   - Klik met de rechtermuisknop op `SnippetLauncher.App.exe` → **Eigenschappen**.
   - Vink onderaan **Blokkering opheffen** aan en klik **OK**.
4. Dubbelklik `SnippetLauncher.App.exe`. Bij eerste start verschijnt de **First-run wizard** — volg de stappen om een snippet-map te kiezen en eventueel een Git-remote te koppelen.
5. Maak optioneel een snelkoppeling op je bureaublad of in het Start Menu.

## Gebruik

| Actie | Standaard sneltoets |
|---|---|
| Zoek-popup openen | **Ctrl+Shift+Space** |
| Snippet toevoegen vanuit klembord | **Ctrl+Shift+N** |

In de zoek-popup:
- Typ om te zoeken (fuzzy matching).
- **↑/↓** om door resultaten te navigeren.
- **Enter** kopieert de geselecteerde snippet en sluit de popup.
- **Esc** sluit de popup zonder actie.

## Placeholders

Snippets kunnen dynamische velden bevatten met **enkele accolades**: `{naam}`. Bij het kopiëren vraagt de app om waarden voor elk niet-built-in veld in te vullen.

| Token | Betekenis |
|---|---|
| `{date}` | Huidige datum (`2026-04-29`) |
| `{time}` | Huidige tijd (`14:35`) |
| `{clipboard}` | Klembord-inhoud op het moment dat je de snippet kiest |
| `{eigen_naam}` | Eigen veld — app vraagt je een waarde in te vullen |
| `{{` of `}}` | Letterlijke `{` of `}` |

Voorbeeld-snippet:

```markdown
Hoi {voornaam},

Hartelijk dank voor je bericht. We bellen je terug op {date} rond {time}.
```

Bij gebruik vraagt de app om een waarde voor `voornaam`. `{date}` en `{time}` worden automatisch ingevuld.

## Synchronisatie tussen machines

Snippets worden gedeeld via een Git-repository. De app commit en pusht je wijzigingen automatisch, en pullt elke 60 seconden updates van anderen.

```
Machine A                                  Machine B
C:\Users\Alice\...\snippets                C:\Users\Bob\...\snippets
        │                                          │
        └──────► github.com/<org>/snippets ◄──────┘
                  (gedeelde remote)
```

- Lokale paden mogen per machine verschillen — alleen de **inhoud** wordt gesynchroniseerd.
- Bij conflicten (beide kanten bewerkten hetzelfde bestand) wint de remote en blijft een backup van de lokale versie achter in `<snippets-map>/.local/conflicts/`.

## Aan de slag

Snippet Launcher werkt met **elke Git-repo met `.md`-bestanden** als snippet-bibliotheek —
je bent niet gebonden aan een specifieke repo. De app initialiseert zelf een Git-repo
in je gekozen map als die nog leeg is, en sync via een remote is volledig optioneel.

- **Eigen bibliotheek opzetten** (nieuwe of bestaande GitHub-repo, of lokaal-only):
  [docs/setup-new-user.md](docs/setup-new-user.md)
- **Meedoen met een bestaande gedeelde repo** (collaborator op andermans bibliotheek):
  [docs/setup-second-user.md](docs/setup-second-user.md)

## Roadmap

Geplande features staan in [docs/plans/](docs/plans/). Eerstvolgende:

- **v1.1** — In-app update checker ([plan](docs/plans/2026-04-29-feat-update-checker-plan.md))

## Bouwen vanuit broncode

```powershell
# Vereist: .NET 10 SDK
dotnet build src/SnippetLauncher.App/SnippetLauncher.App.csproj -c Release

# Self-contained publish (win-x64)
dotnet publish src/SnippetLauncher.App/SnippetLauncher.App.csproj `
    -c Release --self-contained -r win-x64 `
    -o publish/SnippetLauncher-win-x64
```

Test suite:

```powershell
dotnet test
```

## Logbestanden

`%APPDATA%\SnippetLauncher\log\` — stuur deze mee bij een bugreport.

## Licentie

Privégebruik — geen publieke licentie.
