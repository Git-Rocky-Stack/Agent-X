; Agent-X Windows Installer
; Inno Setup 6 Script
; Copyright (c) 2026 Rocky Elsalaymeh. MIT License.
;
; ─────────────────────────────────────────────────────────────────────────────
; Two build profiles, selected by the AgentXOffline preprocessor flag:
;
;   SLIM (default)       ISCC AgentX-Setup.iss
;     No bundled model → ~180 MB → fits GitHub Releases' 2 GiB per-asset limit.
;     The app downloads the built-in model on first run (BuiltInModelBootstrap),
;     so the model never belongs to the installer's file set and the uninstaller
;     never touches it. Cloud API keys (OpenAI/Anthropic) work immediately.
;
;   OFFLINE              ISCC /DAgentXOffline=1 AgentX-Setup.iss
;     Bundles the ~1.9 GB Llama 3.2 3B GGUF for fully-offline first run. The
;     model file carries the `uninsneveruninstall` flag, so uninstalling Agent-X
;     leaves the model in place — a later reinstall does not re-extract ~2 GB
;     (KNOWN-ISSUE #3). Output exceeds GitHub's per-asset limit and is hosted on
;     Cloudflare R2 (see scripts/publish-offline-installer.ps1).
; ─────────────────────────────────────────────────────────────────────────────

#define MyAppName "Agent-X"
#define MyAppVersion "2.1.2"
#define MyAppPublisher "Rocky Elsalaymeh"
#define MyAppURL "https://github.com/Git-Rocky-Stack/Agent-X"
#define MyAppExeName "AgentX.App.exe"
#define MyAppDescription "Local-First AI Personal Intelligence Hub"
#define BuiltInModelFile "llama-3.2-3b-instruct-q4_k_m.gguf"

; Offline builds bundle the model and get an "-offline" output suffix so the two
; installers never collide in installer-output\.
#ifdef AgentXOffline
  #define OutputSuffix "-offline"
#else
  #define OutputSuffix ""
#endif

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
OutputBaseFilename=AgentX-Setup-{#MyAppVersion}-x64{#OutputSuffix}
; Version resources — AppVersion only populates ProductVersion; VersionInfoVersion
; stamps the Win32 FileVersion so inventory/AV tooling reads a real numeric version
; instead of a blank field (KNOWN-ISSUE #4).
VersionInfoVersion={#MyAppVersion}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoCopyright=Copyright (c) 2026 {#MyAppPublisher}. MIT License.
; Compression
Compression=lzma2/max
SolidCompression=yes
LZMANumBlockThreads=2
; Appearance
WizardStyle=modern
WizardSizePercent=120,120
DisableWelcomePage=no
SetupIconFile=..\src\AgentX.App\Assets\agent_x.ico
; Privileges
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; Architecture
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Uninstaller
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
; Minimum Windows version — must match the app's TargetPlatformMinVersion
; (Windows 10 2004 / build 19041+); installing on older builds would let
; setup succeed but the app fail to launch.
MinVersion=10.0.19041
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
#ifdef AgentXOffline
; OFFLINE profile only: bundle the built-in GGUF AI model (~1.9 GB) for fully
; offline operation. `uninsneveruninstall` keeps it under {localappdata} after an
; uninstall so a reinstall does not have to re-extract ~2 GB (KNOWN-ISSUE #3).
Source: "..\models\{#BuiltInModelFile}"; DestDir: "{localappdata}\AgentX\Models"; Flags: ignoreversion uninsneveruninstall
#endif

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Comment: "{#MyAppDescription}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; Comment: "{#MyAppDescription}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Clean up log files on uninstall (the built-in model is intentionally preserved —
; see the OFFLINE [Files] entry; on SLIM installs the downloaded model likewise
; lives outside the installer's file set and is never tracked here).
Type: files; Name: "{localappdata}\AgentX\Logs\*"
Type: dirifempty; Name: "{localappdata}\AgentX\Logs"

[Code]
// Custom code for the installer

function InitializeSetup(): Boolean;
begin
  Result := True;
#ifdef AgentXOffline
  Log('Agent-X installer starting - OFFLINE profile (model bundled)');
#else
  Log('Agent-X installer starting - SLIM profile (model downloads on first run)');
#endif
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Create the runtime directories the app actually uses. The model lands in
    // Models (bundled on OFFLINE, downloaded on first run on SLIM); logs in Logs.
    // (The previously-created Data\ subdirectory was unused and has been removed —
    // the database lives at {localappdata}\AgentX\agentx.db, KNOWN-ISSUE #5.)
    ForceDirectories(ExpandConstant('{localappdata}\AgentX\Logs'));
    ForceDirectories(ExpandConstant('{localappdata}\AgentX\Models'));
    Log('Created AgentX runtime directories');
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
#ifdef AgentXOffline
WelcomeLabel2=This will install [name/ver] on your computer.%n%nAgent-X is a Local-First AI Personal Intelligence Hub that runs entirely on your device. No cloud, no subscriptions, no data leaving your machine.%n%nIncludes a built-in AI model (Llama 3.2 3B) bundled for fully offline operation. Optionally connect OpenAI or Anthropic API keys for cloud models.%n%nPrerequisites:%n- Windows 10 version 2004 (build 19041) or later%n%nIt is recommended that you close all other applications before continuing.
#else
WelcomeLabel2=This will install [name/ver] on your computer.%n%nAgent-X is a Local-First AI Personal Intelligence Hub that runs entirely on your device. No cloud, no subscriptions, no data leaving your machine.%n%nOn first run, Agent-X can download a built-in AI model (Llama 3.2 3B, ~1.9 GB) for offline use — or connect OpenAI or Anthropic API keys to start immediately.%n%nPrerequisites:%n- Windows 10 version 2004 (build 19041) or later%n%nIt is recommended that you close all other applications before continuing.
#endif
