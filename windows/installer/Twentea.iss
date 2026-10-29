; Per-user installer for Twentea. No admin rights needed.
; Build: iscc /DAppVersion=0.2.0 /DArch=win-x64 /DSource=..\publish\win-x64 /DOutputDir=..\dist Twentea.iss
; The in-app updater runs it with /SILENT, and [Run] starts the new version afterwards.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Arch
  #define Arch "win-x64"
#endif
#ifndef Source
  #define Source "..\publish\" + Arch
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
AppId={{6C3F2A9E-5B1D-4E7A-9C2B-8F4D1E6A7B30}
AppName=Twentea
AppVersion={#AppVersion}
AppVerName=Twentea {#AppVersion}
AppPublisher=Muneeb
AppPublisherURL=https://github.com/mianmuneebajaz/twentea
AppSupportURL=https://github.com/mianmuneebajaz/twentea/issues
AppUpdatesURL=https://github.com/mianmuneebajaz/twentea/releases
DefaultDirName={localappdata}\Programs\Twentea
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=Twentea-{#AppVersion}-{#Arch}-setup
SetupIconFile=..\..\brand\generated\twentea.ico
UninstallDisplayIcon={app}\Twentea.exe
UninstallDisplayName=Twentea
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=force
RestartApplications=no
LicenseFile=..\..\LICENSE
#if Arch == "win-arm64"
ArchitecturesAllowed=arm64
#else
ArchitecturesAllowed=x64compatible
#endif

[Tasks]
Name: "startup"; Description: "Open Twentea when I sign in"; GroupDescription: "Options:"

[Files]
Source: "{#Source}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{userprograms}\Twentea"; Filename: "{app}\Twentea.exe"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Twentea"; ValueData: """{app}\Twentea.exe"""; Tasks: startup; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Twentea"; Flags: uninsdeletevalue dontcreatekey
Root: HKCU; Subkey: "Software\Classes\twentea"; Flags: uninsdeletekey dontcreatekey

[Run]
Filename: "{app}\Twentea.exe"; Description: "Start Twentea"; Flags: nowait postinstall

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM Twentea.exe /F"; Flags: runhidden; RunOnceId: "StopTwentea"
