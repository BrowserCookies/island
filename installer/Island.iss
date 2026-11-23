; Inno Setup 6 script for Island. Build it with installer\build.ps1, which publishes the
; single-file exe first and passes the version in. Output: publish\installer\Island-Setup-<version>.exe
;
; Per-user install (no admin / UAC prompt): %LOCALAPPDATA%\Programs\Island, Start Menu shortcut in
; the user's Programs folder. User data in %APPDATA%\Island is left alone on uninstall.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "Island"
#define AppExe "Island.exe"
#define Root SourcePath + "..\"

[Setup]
AppId={{E3A7C2D4-5B1F-4C8E-9A6D-2F0B7C1E8A43}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0.17763
OutputDir={#Root}publish\installer
OutputBaseFilename=Island-Setup-{#AppVersion}
SetupIconFile={#Root}src\Island\Assets\island.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#Root}publish\win-x64\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im {#AppExe}"; Flags: runhidden; RunOnceId: "StopIsland"

[UninstallDelete]
; Created by Settings > General > Start Island with Windows.
Type: files; Name: "{userstartup}\Island.lnk"

[Code]
// Island lives in the tray with no main window, so stop it before replacing the exe.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
