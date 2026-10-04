# Snippets: praktijkacceptatie na versie 1.1.1

Datum: 2026-10-04. Status: wachten op praktijkfeedback; geen open bouwwerk.

## Lees eerst
- [Uitvoeringsverslag](../reviews/2026-10-03-startup-login-uitvoering.md)
- [Plan](../plans/2026-10-03-fix-startup-login-betrouwbaarheid.md)
- [Release v1.1.1](https://github.com/Joepvw/snippetsapp/releases/tag/v1.1.1)

## Stand
Code en lokale onafhankelijke review afgerond; PR #2 gemerged. 146 Core-tests, één architectuurtest en 19 WPF-scenario’s geslaagd. Release v1.1.1 bevat de wijzigingen en is gepubliceerd met installer, zip en controlesommen. Release-build zonder waarschuwingen/fouten, payloadversie en zipinhoud geverifieerd; gepubliceerde SHA256-digests identiek. Joep meldt dat de installer is uitgevoerd; nog geen resultaten van praktijktests gemeld. Eigen implementatiebranches/worktrees zijn verwijderd; testbewijs en tussenversies bewaard onder de sessiebackup in de dirigent-laag.

## Eerstvolgende stap
Joep gebruikt de geïnstalleerde app: volledig afsluiten via tray, herstarten, onmiddellijk typen in zoekvenster, snippet bewerken/opslaan/herstarten en Nu synchroniseren. Bij haperingen concrete stap, waarneming en versie verzamelen. Volledige koude/warmestart en fysieke invoerlatentie zijn nog niet gemeten.

Yasmina’s pc was niet beschikbaar. Joep heeft haar echte loginproef expliciet uitgesteld. Zodra beschikbaar: juiste GitHub-account en uitnodiging/rechten, expliciet aanmelden, herstart zonder herhaalde login, lezen én schrijven op private testrepo, offline herstel, annuleren en tweegebruikersconflict. Gebruik synthetische snippets. Haar persoonlijke loginlus is nog niet bewezen opgelost.

## Open commando’s
Geen deploy-, merge- of releasecommando’s meer open. Geen automatische reminder: er is geen datum afgesproken. Start bij nieuwe bugmelding met diagnose op versie 1.1.1; maak niet opnieuw de bestaande implementatie.
