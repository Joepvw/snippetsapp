# Git-waarschuwing en eerlijke synchronisatiestatus

Status: implementatie afgerond op 2026-10-03, PR [snippetsapp#1](https://github.com/Joepvw/snippetsapp/pull/1). Release en installerdistributie blijven buiten deze opdracht.

De lokale poorten zijn groen: Core 101 tests, architectuur 1 test, build en format, Core-dekking 83,31%. Zes echte Git-regressies falen op de oorspronkelijke code en slagen met de fix. Een geïsoleerde bronproef controleert echte WPF-views/commands en App→Git met tijdelijke repositories zonder de gebruikersapp te raken. De onafhankelijke review omvat de vier repo-lenzen binnen één reviewerslot. De CI-logger is minimaal hersteld naar de ingebouwde consolelogger, zonder poort of tests te veranderen.

## Context
De veldtest van 2026-05-21 in de installer-onboarding-brainstorm meldt ontbrekende Git for Windows en succesvolle UI-meldingen na mislukte fetches. Deze opdracht behandelt uitsluitend follow-up 1 en 2.

## Approach
De Remote-stap gebruikt dezelfde PATH- en Program Files-detectie als de credentialprovider en toont een installatieaanwijzing bij een ingevulde remote zonder Git. Git-operatiefouten bereiken de wachtende aanroeper via de bestaande workerchannel; bootstrap en pushretry kunnen geen vals succes opleveren. Offline wachtrij en dedicated Git-worker blijven behouden.

De Git-installatieaanwijzing is een waarschuwing: de gebruiker kan verdergaan, ook met een publieke of lokale remote waarvoor Git Credential Manager niet nodig is. Instellingen heeft een vaste klikbare link naar de bestaande setup-uitleg. Structurele OAuth-auth en SmartScreen uit follow-up 3 en 4 blijven open in de oorspronkelijke brainstorm.

## Critical files
- Core/Sync/GitService.cs: voltooiing en fouten van pull/bootstrap/push.
- App/ViewModels/FirstRunWizardViewModel.cs en Remote-weergave: Git-waarschuwing.
- App/ViewModels/SettingsViewModel.cs: begrijpelijke misluktmelding.

## Verification
- Echte tijdelijke Git-repositories voor fetch-, bootstrap-, pushfouten en succesvolle herstelpogingen.
- Build, volledige tests, format en Core-coverage minstens 70%.
- Onafhankelijke lokale review met security-, architecture-, performance- en simplicity-lens, sequentieel wegens agentcapaciteit.
- WPF-controle indien beschikbare tooling dit ondersteunt; geen bestaande gebruikersapp stoppen.
- Merge naar master; release, installer, versiekeuze en OAuth blijven buiten scope.
