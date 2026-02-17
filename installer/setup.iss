; ============================================================
; Minecraft Mod Profile Switcher — Inno Setup Installer Script
; ============================================================
; Requirements:
;   - Inno Setup 6.x  (https://jrsoftware.org/isdl.php)
;   - Run "dotnet publish" first to create the publish\ folder
;   - Then open this .iss file in Inno Setup and click Build → Compile
;
; Alternatively, use the build-installer.ps1 script to automate everything.
; ============================================================

#define MyAppName "Minecraft Mod Profile Switcher"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "ModProfileSwitcher"
#define MyAppURL "https://github.com/modprofileswitcher"
#define MyAppExeName "ModProfileSwitcher.exe"

[Setup]
; Unique application ID — do not change after first release
AppId={{8F3A2B7C-5D1E-4F6A-9B0C-2E8D7F4A1B3E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
; Allow user to choose whether to create a desktop icon
AllowNoIcons=yes
; Output installer file
OutputDir=..\installer\output
OutputBaseFilename=ModProfileSwitcher_Setup_{#MyAppVersion}
; Compression
Compression=lzma2/ultra64
SolidCompression=yes
; Installer UI
WizardStyle=modern
; Require admin for Program Files install, but allow per-user
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; Minimum Windows version (Windows 10)
MinVersion=10.0
; Uninstall
UninstallDisplayName={#MyAppName}
; Architecture
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "launchonstartup"; Description: "Start with Windows (minimized)"; GroupDescription: "Other options:"; Flags: unchecked

[Files]
; Main application executable (self-contained, single-file)
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; License / readme
Source: "..\INSTRUCTIONS.md"; DestDir: "{app}"; DestName: "README.md"; Flags: ignoreversion

[Icons]
; Start Menu shortcut
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"

; Desktop shortcut (optional, user chooses during install)
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Optional: start with Windows
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"" --minimized"; Flags: uninsdeletevalue; Tasks: launchonstartup

[Run]
; Launch after install (optional)
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Clean up any leftover files in the install directory
Type: filesandordirs; Name: "{app}"

[Code]
// Show a friendly message if the user has an older version installed
function InitializeSetup(): Boolean;
begin
  Result := True;
end;
