# Uitvoering: opstarten, invoer en aanmelding

Datum: 2026-10-03. Baseline: `e795fa130e8f643ef15cac9949de67fa8a19ab8f`.

De codeverbeterslag is geïmplementeerd en lokaal gecontroleerd. Yasmina's echte Windows/GitHub-proef is op uitdrukkelijk verzoek van Joep doorgeschoven: “Nee, die proef kan later.” De persoonlijke loginlus is daarmee nog niet bevestigd als opgelost. Een installerrelease vraagt afzonderlijke versiekeuze volgens CLAUDE.md; huidige tag is v1.1.0.

## Uitgevoerd

- U1: achtergrondcredentiallookup zonder interactie, timeout en procesboomcleanup; aparte Aanmelden-actie; veilige URL/contextvalidatie en bevestiging na geverifieerde remote-operatie.
- U2: queues per map/remote, atomaire writes en corruptie bewaren; manual retry reset; eerste upstream en recovery uit lokale commits; staged eerste clone en import met behoud van beide bibliotheken en keuze bij overlap.
- U3: lazy editor, één immutable bibliotheeksnapshot, voorbereide zoekstrings en achtergrondqueries; generation/cancellation beschermt resultaten en Enter; late focuscallbacks laten invoer staan.
- U4: conceptinhoud en placeholders blijven behouden; bewaren/verwerpen/annuleren bij selectie/Nieuw/Quick Add/afsluiten; eigen conceptidentiteit beschermt late saves; pending map pas bij herstart actief.
- U5: gedeelde bestandsgate tussen repository- en Git-worker, clone buiten de gate; alleen publicatie/import/checkout/commit krijgt exclusieve toegang. Saves/deletes tijdens import landen na de swap in de actieve map. Subscriptions, drains, stores en live intervalvalidatie aangepast.
- U6: documentatie, reproduceerbare Core-meetproef en geïsoleerde WPF-scenario's. Echte tweegebruikers-GCM-proef en volledige cold/warm processtart/hotkey-metingen blijven veldwerk.

## Verificatie

- Release-build: 0 waarschuwingen, 0 fouten.
- Volledige tests: 146 Core + 1 architectuur, alle groen.
- Core line coverage: 87.77%, boven de vereiste 70%.
- Gerichte Git-regressies: 25 groen, inclusief gelijktijdig save/delete tijdens import en beide conflictkopieën.
- WPF: 19 gecontroleerde scenario's met echte Views, ViewModels, dispatcher en binding; synthetische snippets, eigen proces, geen bestaande app of persoonlijke bestanden gebruikt.
- WPF aanvullingen: late save na verwerpen/Nieuw houdt de nieuwe identiteit; snapshot bewaart een lege Nieuwe snippet; bounded shutdown achter een werkelijk geblokkeerde Git-worker houdt het lokale bestand intact.
- Onafhankelijke review vond vier concrete races/lifecycleproblemen; de correcties zijn opnieuw door een verse validator gecontroleerd. Externe Claude-route is door automatische goedkeuringscontrole geweigerd; geen diff verstuurd. Lokale review gebruikt deels hergebruikte reviewerthreads vanwege capaciteit; validator was vers en onafhankelijk.

Zie [de WPF-asserties](startup-login-evidence/wpf-scenarios.txt) en [het meetprotocol](startup-login-evidence/README.md).

## Core-meting

Zelfde Windows-laptop, Intel Core 7 240H, 32 GiB RAM, .NET SDK 10.0.203, Release. Drie loads en per load 30 warme querysamples na één warmup. Tabellen tonen mediane laadtijd en de mediaan van de drie query-p95's. De refresh simuleert de volledige gesorteerde editorlijst per event. De nieuwe editor wordt bovendien pas bij openen gemaakt.

| Snippets | Load oud | Load nieuw | Lijstrefreshes | Query oud | Query nieuw |
|---|---|---|---|---|---|
| 0 | 0.33 s | 0.00 s | 0 → 1 | 0.0 ms | 0.0 ms |
| 100 | 4.53 s | 0.04 s | 100 → 1 | 305.0 ms | 6.0 ms |
| 1000 | 13.43 s | 0.20 s | 1000 → 1 | 224.6 ms | 11.3 ms |
| 5000 | 213.28 s | 1.11 s | 5000 → 1 | 1117.3 ms | 38.8 ms |

De machine draaide ook tools/tests, vooral tijdens de baseline. Absolute snelheidsratio's zijn indicatief; het aantal lijstrefreshes is direct vastgesteld. Dit meet Core-load en warme zoekkosten, geen processtart→tray, fysieke hotkey→focus, eerste teken of dispatcherlatentie. De geplande 150/200 ms UI-budgetten zijn daarom nog niet als volledige veldacceptatie bewezen. Rawdata staan naast het protocol.

## Begrensd afsluiten

Nieuwe UI-mutaties stoppen eerst. Repositorydrain plus laatste lokale commit krijgen samen 15 seconden; Git-workerdisposal krijgt daarna maximaal 15 seconden. Clone/fetch/push-callbacks observeren cancellation. Native werk vóór een callback kan nog op de OS-timeout wachten. Bij een timeout sluit de host gecontroleerd af, hervat geen half afgebroken diensten en herhaalt geen onbeperkte repositorydrain. Zolang een worker nog kan leven, blijft de single-instance mutex tot procesexit in bezit; een opvolger start dus niet alvast.

Opgeslagen lokale bestanden blijven beschikbaar. Een onderbroken lokale Git-commit wordt bij openen uit de werkboom hersteld. `.preserved-*`, `.recovery-*`, corruptiekopieën en conflictbackups worden voor herstel bewaard; verwijder deze niet routinematig.

## Open veldacceptatie

Bij beschikbaarheid van Yasmina's pc: juiste GitHub-account en uitnodiging/rechten; expliciet aanmelden, herstart zonder loginlus, lees- én schrijfrechten op private testrepo, offline starten/herstellen, login annuleren en twee clients met conflict. Gebruik synthetische snippets. Daarna cold/warm processtart en fysieke hotkey/invoer meten. Geen persoonlijke fixclaim vóór die proef.
