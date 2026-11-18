; Mau SysInfo installer (Inno Setup 6).
; Build it with build-installer.ps1 in the repository root: it publishes the app and passes the
; version from <Version> in the .csproj, so the setup file is named MauSysInfo-<version>-Setup.exe.

#ifndef AppVersion
  #error Pass the version with /DAppVersion=x.y.z (build-installer.ps1 does this).
#endif
#ifndef SourceExe
  #define SourceExe "..\publish\MauSysInfo.exe"
#endif
#ifndef OutputDir
  #define OutputDir "..\publish"
#endif

#define AppName "Mau SysInfo"
#define AppExe "MauSysInfo.exe"
#define AppSite "https://downloads.kinkuroshideout.com"
; Must match App.RunningMutexName in App.xaml.cs.
#define RunningMutex "MauSysInfoRunning"
#define AppIcon "..\src\MauSysInfo\Assets\app.ico"

[Setup]
; Never change AppId: it is how upgrades find the existing installation.
AppId={{6493A0E8-1709-4FFF-A69F-A6AB360BC0BE}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Mausy
AppPublisherURL={#AppSite}
AppSupportURL={#AppSite}
AppUpdatesURL={#AppSite}
AppCopyright=Copyright (C) 2026 Mausy
VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup
VersionInfoCompany=Mausy

DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
; Program Files for everyone by default; the dialog lets someone without admin rights install just for themselves.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
#if FileExists(AddBackslash(SourcePath) + AppIcon)
SetupIconFile={#AppIcon}
#endif
; Mascot artwork, one file per display scale (100%-250%). Regenerate with make-wizard-images.ps1.
WizardImageFile=wizard\large-164x314.png,wizard\large-192x386.png,wizard\large-246x459.png,wizard\large-273x556.png,wizard\large-328x604.png,wizard\large-355x700.png,wizard\large-410x797.png
WizardSmallImageFile=wizard\small-55x55.png,wizard\small-64x68.png,wizard\small-83x80.png,wizard\small-92x97.png,wizard\small-110x106.png,wizard\small-119x123.png,wizard\small-138x140.png

; Setup and the uninstaller ask to close the app while it runs.
AppMutex={#RunningMutex},Global\{#RunningMutex}

OutputDir={#OutputDir}
OutputBaseFilename=MauSysInfo-{#AppVersion}-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nThe only official download is {#AppSite}. If you got this installer anywhere else, it may have been tampered with: cancel and download it from there.%n%nIt is recommended that you close all other applications before continuing.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Starts as the signed-in user (not elevated), like launching it from the Start menu.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; In-app updates run Setup with /SILENT /RELAUNCH=1 (UpdateService.SilentInstallArguments): start the app again,
; as the signed-in user even when Setup was elevated for a Program Files install.
Filename: "{app}\{#AppExe}"; Flags: nowait runasoriginaluser; Check: RelaunchRequested

[Code]
function RelaunchRequested: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:RELAUNCH|0}') = '1');
end;
