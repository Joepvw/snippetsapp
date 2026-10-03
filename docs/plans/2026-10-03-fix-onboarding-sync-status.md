# Git-waarschuwing en eerlijke synchronisatiestatus

Status: in uitvoering door nachtrun 2026-10-03.

## Context
De veldtest van 2026-05-21 in de installer-onboarding-brainstorm meldt ontbrekende Git for Windows en succesvolle UI-meldingen na mislukte fetches. Deze opdracht behandelt uitsluitend follow-up 1 en 2.

## Approach
De Remote-stap gebruikt dezelfde PATH- en Program Files-detectie als de credentialprovider en toont een installatieaanwijzing bij een ingevulde remote zonder Git. Git-operatiefouten bereiken de wachtende aanroeper via de bestaande workerchannel; bootstrap en pushretry kunnen geen vals succes opleveren. Offline wachtrij en dedicated Git-worker blijven behouden.

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
