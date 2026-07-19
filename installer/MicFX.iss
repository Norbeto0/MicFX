; Inno Setup script — builds MicFX-Setup.exe from the published single-file exe.
; Compiled in CI with: ISCC.exe /DMyAppVersion=x.y.z installer\MicFX.iss

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

[Setup]
AppId={{8E7A2C51-9D1B-4F2E-B1B0-5C0E1D9FA1CE}
AppName=MicFX
AppVersion={#MyAppVersion}
AppPublisher=Norbeto0
AppPublisherURL=https://github.com/Norbeto0/MicFX
DefaultDirName={autopf}\MicFX
DefaultGroupName=MicFX
UninstallDisplayIcon={app}\MicFX.exe
OutputDir=Output
OutputBaseFilename=MicFX-Setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Per-user install: no admin prompt, and matches the HKCU run-on-boot entry.
PrivilegesRequired=lowest
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; Flags: unchecked

[Files]
Source: "..\publish\MicFX.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\MicFX"; Filename: "{app}\MicFX.exe"
Name: "{autodesktop}\MicFX"; Filename: "{app}\MicFX.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\MicFX.exe"; Description: "Launch MicFX"; Flags: nowait postinstall skipifsilent
; In-app auto-update runs setup with /VERYSILENT /AUTORELAUNCH=1 — relaunch MicFX afterwards.
Filename: "{app}\MicFX.exe"; Parameters: "--updated"; Flags: nowait; Check: IsAutoRelaunch

[Code]
function IsAutoRelaunch: Boolean;
begin
  Result := ExpandConstant('{param:AUTORELAUNCH|0}') = '1';
end;

[UninstallRun]
; nothing — settings in %APPDATA%\MicFX are left in place on purpose
