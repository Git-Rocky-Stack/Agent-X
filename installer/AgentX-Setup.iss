; Agent-X Windows Installer
; Inno Setup 6 Script
; Copyright (c) 2026 Rocky Stack. All rights reserved.

#define MyAppName "Agent-X"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Rocky Stack"
#define MyAppURL "https://rockystack.com"
#define MyAppExeName "AgentX.App.exe"
#define MyAppDescription "Local-First AI Personal Intelligence Hub"

[Setup]
AppId={{B3F8A2D1-7E4C-4A9B-8F6D-1C5E3A2B9D7F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; Output configuration
OutputDir=..\installer-output
OutputBaseFilename=AgentX-Setup-{#MyAppVersion}-x64
; Compression
Compression=lzma2/ultra64
SolidCompression=yes
LZMANumBlockThreads=4
; Appearance
WizardStyle=modern
WizardSizePercent=120,120
DisableWelcomePage=no
; Privileges
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; Architecture
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Uninstaller
UninstallDisplayName={#MyAppName}
; Minimum Windows version (Windows 10 1903+)
MinVersion=10.0.18362
; Misc
DisableProgramGroupPage=yes
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startmenuicon"; Description: "Create a Start Menu shortcut"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Install all published files
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "{#MyAppDescription}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; Comment: "{#MyAppDescription}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Clean up log files on uninstall
Type: files; Name: "{localappdata}\AgentX\Logs\*"
Type: dirifempty; Name: "{localappdata}\AgentX\Logs"

[Code]
// Custom code for the installer

function InitializeSetup(): Boolean;
begin
  Result := True;
  Log('Agent-X installer starting - self-contained deployment');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Create the AgentX data directory structure
    ForceDirectories(ExpandConstant('{localappdata}\AgentX\Logs'));
    ForceDirectories(ExpandConstant('{localappdata}\AgentX\Data'));
    ForceDirectories(ExpandConstant('{localappdata}\AgentX\Models'));
    Log('Created AgentX data directories');
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  NeedsRestart := False;

  // Kill any running instances of Agent-X before installing
  if Exec('taskkill', '/F /IM AgentX.App.exe', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    Log('Terminated running Agent-X instance (exit code: ' + IntToStr(ExitCode) + ')')
  else
    Log('No running Agent-X instance found or taskkill failed');
end;

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nAgent-X is a Local-First AI Personal Intelligence Hub that runs entirely on your device. No cloud, no subscriptions, no data leaving your machine.%n%nPrerequisites:%n- Windows 10 version 1903 or later%n- Ollama (recommended for AI features)%n%nIt is recommended that you close all other applications before continuing.
