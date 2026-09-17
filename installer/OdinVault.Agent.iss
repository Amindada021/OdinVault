#define MyAppName "OdinVault Agent"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "OdinVault"
#define MyAppExeName "OdinVault.Agent.exe"

[Setup]
AppId={{D83DA5AC-3F02-4A75-A2E1-36F7A690E712}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\OdinVault\Agent
DefaultGroupName=OdinVault
OutputDir=output
OutputBaseFilename=OdinVault-Agent-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "..\artifacts\agent\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{commonappdata}\OdinVault"; Permissions: users-modify
Name: "{commonappdata}\OdinVault\storage"; Permissions: users-modify
Name: "{commonappdata}\OdinVault\replicas"; Permissions: users-modify

[Run]
Filename: "{sys}\sc.exe"; Parameters: "create OdinVaultAgent binPath= \"{app}\{#MyAppExeName}\" start= auto DisplayName= \"OdinVault Agent\""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "description OdinVaultAgent \"OdinVault database backup and replication agent\""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=\"OdinVault Agent\" dir=in action=allow protocol=TCP localport=5188"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "start OdinVaultAgent"; Flags: runhidden waituntilterminated

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop OdinVaultAgent"; Flags: runhidden waituntilterminated skipifdoesntexist
Filename: "{sys}\sc.exe"; Parameters: "delete OdinVaultAgent"; Flags: runhidden waituntilterminated skipifdoesntexist
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=\"OdinVault Agent\""; Flags: runhidden waituntilterminated
