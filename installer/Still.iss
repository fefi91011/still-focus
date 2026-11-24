#define MyAppName "Still"
#define MyAppVersion "0.2.2"
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
UsePreviousAppDir=yes
UsePreviousTasks=yes
UsePreviousLanguage=yes
DisableDirPage=yes
DisableProgramGroupPage=yes
ShowLanguageDialog=no
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
CloseApplications=yes
RestartApplications=no
VersionInfoVersion=0.2.2.0
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
Filename: "{app}\{#MyAppExeName}"; Parameters: "--cleanup-hosts"; Flags: runhidden waituntilterminated; StatusMsg: "Alte Website-Sperre wird entfernt ..."
Filename: "{app}\{#MyAppExeName}"; Flags: nowait runasoriginaluser; Check: ShouldRestartAfterUpdate
Filename: "{app}\{#MyAppExeName}"; Description: "Still jetzt starten"; Flags: nowait postinstall skipifsilent

[Code]
function ShouldRestartAfterUpdate: Boolean;
begin
  { Silent updates pass /RESTARTSTILL=1. Normal setups use the postinstall entry. }
  Result := WizardSilent and (ExpandConstant('{param:RESTARTSTILL|0}') = '1');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  AppExe: String;
  Problems: String;
begin
  if CurUninstallStep <> usUninstall then
    Exit;

  AppExe := ExpandConstant('{app}\{#MyAppExeName}');
  if not FileExists(AppExe) then
    Exit;

  { --cleanup returns 1 = hosts failed, 2 = autostart failed, 4 = unexpected. }
  if not Exec(AppExe, '--cleanup', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('Still konnte nicht gestartet werden, um alte hosts-Eintraege und Autostart zu bereinigen.' + #13#10 +
           'Bitte entferne die Still-Eintraege in der hosts-Datei und die geplanten Aufgaben' + #13#10 +
           '"Still Autostart" und "Still Watchdog" danach manuell.',
           mbError, MB_OK);
    Exit;
  end;

  if ResultCode = 0 then
    Exit;

  Problems := '';
  if (ResultCode and 1) <> 0 then
    Problems := Problems + '- Die hosts-Datei enthaelt noch alte Still-Eintraege.' + #13#10;
  if (ResultCode and 2) <> 0 then
    Problems := Problems + '- Die Autostart-Aufgaben "Still Autostart" und "Still Watchdog" konnten nicht entfernt werden.' + #13#10;
  if (ResultCode and 4) <> 0 then
    Problems := Problems + '- Die Bereinigung ist unerwartet fehlgeschlagen.' + #13#10;

  MsgBox('Diese Systemaenderungen konnten nicht zurueckgenommen werden:' + #13#10 + #13#10 +
         Problems + #13#10 +
         'Starte Still einmal als Administrator und nutze dort die Deinstallation erneut,' + #13#10 +
         'oder entferne die Eintraege manuell in der hosts-Datei und der Aufgabenplanung.',
         mbError, MB_OK);
end;
