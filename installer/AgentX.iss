; Agent-X Installer Script for Inno Setup 6
; Generates a single installer with x86, x64, and ARM64 architecture options

#define AppName "Agent-X"
#define AppVersion "1.0.0"
#define AppPublisher "Strategia"
#define AppURL "https://strategia-x.com"
#define AppExeName "AgentX.App.exe"

[Setup]
AppId={{A1B2C3D4-E5F6-4A5B-8C7D-9E0F1A2B3C4D5}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={autopf}\Agent-X
DefaultGroupName=Agent-X
AllowNoIcons=yes
OutputDir=..\..\installer
OutputBaseFilename=Agent-X-Setup-v{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x86 x64 arm64
ArchitecturesInstallIn64BitMode=x64 arm64
MinVersion=10.0.19041
UninstallDisplayIcon={app}\Assets\agent_x.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop icon"; GroupDescription: "Additional icons:"
Name: "quicklaunchicon"; Description: "Create a quick launch icon"; GroupDescription: "Additional icons:"
Name: "autostart"; Description: "Start Agent-X automatically on Windows startup"; GroupDescription: "Startup:"

[Files]
; x64 files
Source: "..\publish\x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Check: IsX64()
; x86 files
Source: "..\publish\x86\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Check: IsX86()
; ARM64 files - if built
; Source: "..\publish\arm64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Check: IsARM64()

; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
Name: "{group}\Agent-X"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall Agent-X"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Agent-X"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\Agent-X"; Filename: "{app}\{#AppExeName}"; Tasks: quicklaunchicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch Agent-X"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#AppExeName}"; Parameters: "--register-autostart"; Tasks: autostart; Flags: runhidden

[UninstallDelete]
Type: filesandordirs; Name: "{app}\_*"
Type: filesandordirs; Name: "{localappdata}\Agent-X"

[Registry]
; Register for autostart if task is selected
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AgentX"; ValueData: """{app}\{#AppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue

; File associations for supported document types
Root: HKCR; Subkey: ".pdf\OpenWithProgids"; ValueType: string; ValueName: "AgentX.pdf"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCR; Subkey: "AgentX.pdf"; ValueType: string; ValueName: ""; ValueData: "Agent-X PDF Document"; Flags: uninsdeletekey
Root: HKCR; Subkey: "AgentX.pdf\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},0"
Root: HKCR; Subkey: "AgentX.pdf\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""

Root: HKCR; Subkey: ".docx\OpenWithProgids"; ValueType: string; ValueName: "AgentX.docx"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCR; Subkey: "AgentX.docx"; ValueType: string; ValueName: ""; ValueData: "Agent-X Word Document"; Flags: uninsdeletekey
Root: HKCR; Subkey: "AgentX.docx\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},1"
Root: HKCR; Subkey: "AgentX.docx\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""

Root: HKCR; Subkey: ".txt\OpenWithProgids"; ValueType: string; ValueName: "AgentX.txt"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCR; Subkey: "AgentX.txt"; ValueType: string; ValueName: ""; ValueData: "Agent-X Text Document"; Flags: uninsdeletekey
Root: HKCR; Subkey: "AgentX.txt\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},2"
Root: HKCR; Subkey: "AgentX.txt\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""

Root: HKCR; Subkey: ".md\OpenWithProgids"; ValueType: string; ValueName: "AgentX.md"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCR; Subkey: "AgentX.md"; ValueType: string; ValueName: ""; ValueData: "Agent-X Markdown Document"; Flags: uninsdeletekey
Root: HKCR; Subkey: "AgentX.md\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExeName},3"
Root: HKCR; Subkey: "AgentX.md\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""

[Code]
function IsX64(): Boolean;
begin
  Result := Is64BitInstallMode and (ProcessorArchitecture = paX64);
end;

function IsX86(): Boolean;
begin
  Result := not Is64BitInstallMode and (ProcessorArchitecture = paX86);
end;

function IsARM64(): Boolean;
begin
  Result := Is64BitInstallMode and (ProcessorArchitecture = paARM64);
end;

function InitializeSetup(): Boolean;
begin
  // Ensure Windows 10 version 19041 or later
  if not UsingWinNT() then
  begin
    MsgBox('Agent-X requires Windows 10 (version 19041) or later.', mbError, MB_OK);
    Result := False;
  end
  else
    Result := True;
end;
