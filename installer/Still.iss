#define MyAppName "Still"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "Still"
#define MyAppExeName "Still.App.exe"
#define MyAppId "{{B6E1F3A1-6C2D-4A6E-9E3B-2F1B7D4C8A90}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Still
DefaultGroupName=Still
DisableProgramGroupPage=yes
OutputDir=..\dist\installer
OutputBaseFilename=StillSetup-{#MyAppVersion}
SetupIconFile=..\src\Still.App\Assets\still.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=no
RestartApplications=no
VersionInfoVersion=0.1.0.0
VersionInfoProductName={#MyAppName}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Still - Fokus-App fuer Windows

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\artifacts\installer-staging\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\Still"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "Still oeffnen"
Name: "{autodesktop}\Still"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Still jetzt starten"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "CleanupSystemChanges"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""Still Autostart"""; Flags: runhidden; RunOnceId: "RemoveStillAutostart"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""Still Watchdog"""; Flags: runhidden; RunOnceId: "RemoveStillWatchdog"
