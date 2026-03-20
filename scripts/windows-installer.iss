#ifndef AppVersion
  #define AppVersion "0.1.0-local"
#endif

#ifndef SourceDir
  #error SourceDir is not defined. Pass /DSourceDir=... to ISCC.
#endif

#ifndef OutputDir
  #define OutputDir "."
#endif

#define MyAppName "SimplerJiang AI Agent"
#define MyAppPublisher "SimplerJiang"
#define MyAppExeName "SimplerJiangAiAgent.Desktop.exe"

[Setup]
AppId={{6D34B98D-6E3B-46A8-A4C6-0C1F0BA089C8}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\SimplerJiangAiAgent
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
OutputDir={#OutputDir}
OutputBaseFilename=SimplerJiangAiAgent-Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "安装完成后立即启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent