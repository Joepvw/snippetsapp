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

## Stap 1 — Snippet Launcher installeren

1. Ga naar de Releases-pagina: **https://github.com/Joepvw/snippetsapp/releases/latest**
2. Download `SnippetLauncher-Setup-vX.Y.Z.exe` (één bestand — geen zip uitpakken).
3. Dubbelklik het bestand. Windows SmartScreen waarschuwt dat de app niet ondertekend is:
   klik op **Meer informatie** → **Toch uitvoeren**.
4. Klik door de installer (3 schermen). Standaard wordt Snippet Launcher per gebruiker
   geïnstalleerd in `%LOCALAPPDATA%\Programs\SnippetLauncher\` zonder admin-rechten.
   Vink eventueel "Snelkoppeling op bureaublad maken" aan. Aan het einde kun je
   "Snippet Launcher starten" aanlaten.

> **Updates** verlopen later vanzelf: zodra er een nieuwe versie is, verschijnt er
> een melding in het systeemvak met een link naar de nieuwe download. Draai die
> opnieuw en je hebt de update.

> **Bestaande zip-installatie?** De installer ziet die en vraagt eenmalig om
> bevestiging. Je snippets en instellingen blijven behouden — alleen de programma-bestanden worden vervangen.

> **Liever de oude zip-route?** Ook de `.zip` met de losse bestanden blijft als
> alternatief op de Release-pagina staan voor power-users die geen installer willen.

## Stap 2 — Repository-URL ophalen

Vraag de eigenaar van de gedeelde repo om de HTTPS-URL en (als de repo privé is)
om je toe te voegen als collaborator.

Voor privé-repo's vraagt Git Credential Manager bij de eerste sync om inloggegevens.
Gebruik een **Personal Access Token (PAT)** als wachtwoord
([GitHub PAT aanmaken](https://github.com/settings/tokens) — minimaal `repo`-scope).

> Wil je de repo liever zelf eerst klonen via de terminal? Dat mag ook —
> kies dan in Stap 3 de geklonde map en laat het Remote-veld leeg.

## Stap 3 — First-run wizard doorlopen

Start `SnippetLauncher.App.exe`. De wizard opent automatisch:

| Stap | Actie |
|------|-------|
| Welkom | Klik **Aan de slag** |
| Snippets-map | Klik **Bladeren…** en kies een **lege map buiten OneDrive/Dropbox** (bv. `C:\Users\JouwNaam\Documents\Snippets`). De app maakt 'm zonodig aan. ⚠️ Zie waarschuwing hieronder. |
| Remote | Plak de HTTPS-URL van de gedeelde repo. Bij een lege map kloont de app automatisch bij voltooien. |
| Hotkeys | Bevestig of pas de sneltoetsen aan |
| Klaar | Klik **Voltooien** — de eerste sync start direct daarna |

> ⚠️ **Plaats de snippets-map NIET in OneDrive, Dropbox, Google Drive of een andere cloudsync-map.**
> Snippet Launcher gebruikt zelf Git om met GitHub te synchroniseren. Cloudsync-tools synchroniseren
> óók de `.git/`-mapinhoud, en die botsen dan met Git's eigen file-locks (`index.lock`).
> Dat veroorzaakt corrupte commits, vastlopende sync en willekeurige fouten bij afsluiten.
> Kies een gewone lokale map zoals `C:\Users\<naam>\Documents\Snippets` — GitHub is al je back-up.

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
