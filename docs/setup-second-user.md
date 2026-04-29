# Snippet Launcher — Meedoen met een bestaande snippets-repo

Deze handleiding helpt je om Snippet Launcher op te zetten op een Windows-computer
en aan te sluiten op een **bestaande gedeelde** snippets-repository — bijvoorbeeld
om met een collega of tussen je eigen machines dezelfde bibliotheek te delen.

> Wil je een **nieuwe eigen** bibliotheek beginnen?
> Zie [setup-new-user.md](setup-new-user.md).

## Vereisten

- Windows 10 22H2 of nieuwer (of Windows 11)
- [Git for Windows](https://git-scm.com/download/win) geïnstalleerd
  — vereist voor synchronisatie en Git Credential Manager
- Toegang tot de gedeelde repo (collaborator-rechten als hij privé is)

## Stap 1 — Snippet Launcher downloaden

1. Download de nieuwste `.zip` van de Releases-pagina:
   **https://github.com/Joepvw/snippetsapp/releases/latest**
2. Pak de zip uit naar `%LOCALAPPDATA%\Programs\SnippetLauncher\`
   (typ dat pad in de adresbalk van Verkenner — Windows maakt de mappen automatisch aan).
   Dit is de standaardlocatie voor user-scoped apps; geen admin-rechten nodig en updates kun je later eenvoudig over deze map heen uitpakken.
3. Klik met **rechts** op `SnippetLauncher.App.exe` → **Eigenschappen** →
   onderin bij "Beveiliging" → vink **Blokkering opheffen** aan → OK.
   _(Windows SmartScreen blokkeert anders het starten van ongesigneerde apps.)_
4. Maak optioneel een snelkoppeling naar `SnippetLauncher.App.exe` op je bureaublad of in het Start Menu.

## Stap 2 — Repository klonen

Vraag de eigenaar van de gedeelde repo om de HTTPS-URL en (als de repo privé is)
om je toe te voegen als collaborator.

Open een terminal (PowerShell of Git Bash) en kloon de repo naar een lokale map naar keuze:

```bash
git clone https://github.com/<eigenaar>/<repo>.git C:\Users\JouwNaam\Documents\Snippets
```

Pas het doelpad aan naar wat jij wil — dit is jouw **lokale werkkopie**. De snippet-inhoud
wordt via GitHub gesynchroniseerd, maar het lokale pad mag per machine verschillen.

Voor privé-repo's vraagt Git Credential Manager om inloggegevens. Gebruik een
**Personal Access Token (PAT)** als wachtwoord
([GitHub PAT aanmaken](https://github.com/settings/tokens) — minimaal `repo`-scope).

## Stap 3 — First-run wizard doorlopen

Start `SnippetLauncher.App.exe`. De wizard opent automatisch:

| Stap | Actie |
|------|-------|
| Welkom | Klik **Aan de slag** |
| Snippets-map | Klik **Bladeren…** en navigeer naar de geklonde map (bv. `C:\Users\JouwNaam\Documents\Snippets`) |
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
