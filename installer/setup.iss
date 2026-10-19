#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef PayloadDir
  #error PayloadDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#ifndef ProjectDir
  #error ProjectDir is required
#endif

[Setup]
AppId=SSHServersManager.zhu-0731
AppName=SSH Servers Manager
AppVersion={#AppVersion}
AppPublisher=zhu-0731
AppPublisherURL=https://github.com/zhu-0731/ssh-servers-manager
AppSupportURL=https://github.com/zhu-0731/ssh-servers-manager/issues
AppUpdatesURL=https://github.com/zhu-0731/ssh-servers-manager/releases
DefaultDirName={localappdata}\Programs\SSH Servers Manager
DefaultGroupName=SSH Servers Manager
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
DisableWelcomePage=no
DisableDirPage=no
DisableProgramGroupPage=no
SetupIconFile={#ProjectDir}\src\SshManager.App\Assets\app.ico
UninstallDisplayIcon={app}\SshServersManager.exe
OutputDir={#OutputDir}
OutputBaseFilename=SSH-Servers-Manager-v{#AppVersion}-win-x64-Setup
Compression=lzma2/normal
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
SetupMutex=SSHServersManager.zhu-0731.Setup
UninstallDisplayName=SSH Servers Manager
VersionInfoVersion={#AppVersion}

[Languages]
Name: "chinesesimp"; MessagesFile: "{#ProjectDir}\installer\languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SSH Servers Manager"; Filename: "{app}\SshServersManager.exe"; WorkingDir: "{app}"
Name: "{group}\Uninstall SSH Servers Manager"; Filename: "{uninstallexe}"
Name: "{autodesktop}\SSH Servers Manager"; Filename: "{app}\SshServersManager.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\SshServersManager.exe"; Description: "{cm:LaunchProgram,SSH Servers Manager}"; Flags: nowait postinstall skipifsilent

; No UninstallDelete entries: user configuration, credentials and unknown files are retained.
