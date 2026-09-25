#define MyAppName "OdinVault"
#ifndef MyAppVersion
  #define MyAppVersion "0.2.10"
#endif
#define MyAppPublisher "OdinVault"
#define AgentExeName "OdinVault.Agent.exe"
#define ManagerExeName "OdinVault.Manager.exe"
#define ServiceName "OdinVaultAgent"

[Setup]
AppId={{D83DA5AC-3F02-4A75-A2E1-36F7A690E712}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\OdinVault
DefaultGroupName=OdinVault
OutputDir=output
OutputBaseFilename=OdinVault-Setup-v{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\Manager\{#ManagerExeName}

[Files]
Source: "..\artifacts\agent\*"; DestDir: "{app}\Agent"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\manager\*"; DestDir: "{app}\Manager"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{commonappdata}\OdinVault"
Name: "{commonappdata}\OdinVault\storage"
Name: "{commonappdata}\OdinVault\replicas"

[Icons]
Name: "{group}\OdinVault Manager"; Filename: "{app}\Manager\{#ManagerExeName}"
Name: "{autodesktop}\OdinVault Manager"; Filename: "{app}\Manager\{#ManagerExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "ایجاد میانبر OdinVault Manager روی Desktop"; GroupDescription: "میانبرها:"; Flags: unchecked

[Run]
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} binPath= ""{app}\Agent\{#AgentExeName}"" start= auto DisplayName= ""OdinVault Agent"""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "description {#ServiceName} ""OdinVault database backup and replication agent"""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "failure {#ServiceName} reset= 86400 actions= restart/60000/restart/300000/restart/900000"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "failureflag {#ServiceName} 1"; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""OdinVault Agent"" dir=in action=allow protocol=TCP localport=5188"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; Flags: runhidden waituntilterminated
Filename: "{app}\Manager\{#ManagerExeName}"; Description: "اجرای OdinVault Manager"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden waituntilterminated skipifdoesntexist
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden waituntilterminated skipifdoesntexist
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""OdinVault Agent"""; Flags: runhidden waituntilterminated

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1000);
end;
