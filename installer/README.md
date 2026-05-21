# Installer

`SnippetLauncher.iss` is the Inno Setup 6 script that produces the
per-user `SnippetLauncher-Setup-vX.Y.Z.exe` distributed on
[GitHub Releases](https://github.com/Joepvw/snippetsapp/releases).

## Build

Install **Inno Setup 6** once from <https://jrsoftware.org/isinfo.php>.

The release skill (`.claude/skills/release/SKILL.md`) compiles the
installer automatically as part of `gh release create`. To build manually:

```powershell
dotnet publish src/SnippetLauncher.App/SnippetLauncher.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -o publish/SnippetLauncher-win-x64

& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" `
  /DAppVersion=1.0.5 installer\SnippetLauncher.iss
```

Output lands in `publish/SnippetLauncher-Setup-v1.0.5.exe`.

## AppId

The `AppId` GUID in the script is the install's identity for upgrades and
uninstall. **Never change it.** Changing it leaves users with orphan installs
and two entries in "Apps and Features".
