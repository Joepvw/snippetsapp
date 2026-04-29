# Snippet Launcher — Instellen voor een tweede gebruiker

Deze handleiding helpt je om Snippet Launcher op te zetten op een tweede Windows-computer,
zodat je dezelfde snippets-bibliotheek deelt via een Git-repository.

## Vereisten

- Windows 10 22H2 of nieuwer (of Windows 11)
- [Git for Windows](https://git-scm.com/download/win) geïnstalleerd
  — vereist voor synchronisatie en Git Credential Manager

## Stap 1 — Snippet Launcher downloaden

1. Haal de nieuwste `.zip` release op (gedeeld door de eerste gebruiker of via jullie interne link).
2. Pak de zip uit naar een map naar keuze, bijv. `C:\Tools\SnippetLauncher\`.
3. Klik met **rechts** op `SnippetLauncher.App.exe` → **Eigenschappen** →
   onderin bij "Beveiliging" → vink **Blokkering opheffen** aan → OK.
   _(Windows SmartScreen blokkeert anders het starten van ongesigneerde apps.)_

## Stap 2 — Repository klonen

Open een terminal (PowerShell of Git Bash) en kloon de gedeelde repository:

```bash
git clone https://github.com/jouwnaam/snippets.git C:\Users\JouwNaam\Snippets
```

Vervang de URL door de juiste remote URL (vraag die op bij de eerste gebruiker).

Als de repository privé is, vraagt Git Credential Manager automatisch om inloggegevens.
Gebruik een **Personal Access Token (PAT)** als wachtwoord
([GitHub PAT aanmaken](https://github.com/settings/tokens)).

## Stap 3 — First-run wizard doorlopen

Start `SnippetLauncher.App.exe`. De wizard opent automatisch:

| Stap | Actie |
|------|-------|
| Welkom | Klik **Aan de slag** |
| Snippets-map | Klik **Bladeren…** en navigeer naar de geklonde map (`C:\Users\JouwNaam\Snippets`) |
| Remote | De remote is al geconfigureerd via `git clone`; je kunt dit veld leeg laten |
| Hotkeys | Bevestig of pas de sneltoetsen aan |
| Klaar | Klik **Voltooien** |

## Stap 4 — Werking controleren

- Druk op **Ctrl+Shift+Space** (of jouw gekozen hotkey) om de zoek-popup te openen.
- Typ een snippet-naam om te zoeken.
- Klik het tray-icoon (rechtsonder in de taakbalk) met rechts voor alle opties.

## Synchronisatie

Snippet Launcher haalt automatisch wijzigingen op van de remote (standaard elke 60 seconden).
Wijzigingen die jij aanbrengt worden automatisch gepusht.

Bij een conflict (beide gebruikers bewerken hetzelfde bestand) wint de remote versie.
Een backup van de lokale versie wordt opgeslagen in `<snippets-map>/.local/conflicts/`.

## Problemen oplossen

| Probleem | Oplossing |
|----------|-----------|
| App start niet (SmartScreen) | Zie Stap 1 — blokkering opheffen |
| Hotkey werkt niet | Ga naar tray → Instellingen → Sneltoetsen en kies een andere combinatie |
| Git auth-fout | Verwijder de opgeslagen credential via **Credential Manager** in Windows en probeer opnieuw |
| Snippets worden niet gesynchroniseerd | Controleer of Git for Windows is geïnstalleerd en of de remote URL klopt (tray → Instellingen → Opslag) |

## Logbestanden

Logbestanden staan in `%APPDATA%\SnippetLauncher\log\`. Stuur deze mee bij een bugreport.
