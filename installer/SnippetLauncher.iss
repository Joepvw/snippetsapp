; SnippetLauncher.iss — Inno Setup 6 script.
;
; Compile from the repo root with:
;   iscc /DAppVersion=1.0.5 installer\SnippetLauncher.iss
;
; The `release` skill in .claude/skills/release/ runs this automatically.
;
; The build expects publish/SnippetLauncher-win-x64/ to exist; produce it with:
;   dotnet publish src/SnippetLauncher.App/SnippetLauncher.App.csproj `
;     -c Release -r win-x64 --self-contained true -o publish/SnippetLauncher-win-x64

#define AppName "SnippetLauncher"
#define AppPublisher "Joep van Weert"
#define AppExeName "SnippetLauncher.App.exe"
#define AppPublishDir "..\publish\SnippetLauncher-win-x64"

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
; AppId DO NOT CHANGE — this GUID identifies the install for upgrades and
; uninstall. Wijzigen breekt updates voor alle bestaande gebruikers (orphan
; install + dubbele uninstall-entries in "Apps and Features").
AppId={{808412CC-4EDA-45DB-8871-550B58BA589C}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/Joepvw/snippetsapp
AppSupportURL=https://github.com/Joepvw/snippetsapp/issues
AppUpdatesURL=https://github.com/Joepvw/snippetsapp/releases

; Per-user install by default (no UAC prompt). Advanced users can request
; per-machine install in Program Files via the elevation dialog — that route
; gives NTFS protection against DLL-planting in {app} for the rare hostile
; environment where another process under the same user is untrusted.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
UsePreviousAppDir=yes

; Restart Manager handles a running app gracefully: the install asks Windows
; to close the running process via the same mechanism that Task Manager uses
; (WM_QUERYENDSESSION). Our single-instance mutex releases automatically on
; process exit, so the new install can write the .exe / .dll without issue.
CloseApplications=force
RestartApplications=yes

; Heavy compression — self-contained .NET payloads have a lot of duplicate
; strings across DLLs, so solid LZMA2 ultra64 saves ~50 MB on a typical
; publish output.
Compression=lzma2/ultra64
SolidCompression=yes

Uninstallable=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}

OutputDir=..\publish
OutputBaseFilename={#AppName}-Setup-v{#AppVersion}

WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "dutch"; MessagesFile: "compiler:Languages\Dutch.isl"

[Tasks]
Name: "desktopicon"; \
  Description: "Snelkoppeling op bureaublad maken"; \
  GroupDescription: "Extra snelkoppelingen:"; \
  Flags: unchecked

[Files]
Source: "{#AppPublishDir}\*"; DestDir: "{app}"; \
  Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; \
  Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; \
  Description: "{#AppName} starten"; \
  Flags: postinstall nowait skipifsilent

[Code]
function IsInnoManagedInstall(): Boolean;
begin
  Result :=
    RegKeyExists(HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1')
    or RegKeyExists(HKEY_LOCAL_MACHINE,
      'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1');
end;

function InitializeSetup(): Boolean;
var
  Path: string;
begin
  Result := True;
  Path := ExpandConstant('{localappdata}\Programs\SnippetLauncher');

  // First time the user runs the installer over an existing zip extraction,
  // warn them. After that the Inno uninstall key exists, this branch is
  // skipped, and silent over-the-top updates work normally.
  if DirExists(Path) and not IsInnoManagedInstall() then
  begin
    if MsgBox(
        'Er staat al een SnippetLauncher-installatie in:' + #13#10 +
        Path + #13#10#13#10 +
        'Deze wordt vervangen door de installer-versie. Je snippets en ' +
        'instellingen (in %APPDATA%\SnippetLauncher) blijven behouden.' + #13#10#13#10 +
        'Doorgaan?',
        mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;
