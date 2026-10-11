#ifndef PublishDir
  #define PublishDir "..\publish"
#endif
#ifndef OutputDir
  #define OutputDir ".."
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{8F4E2D1A-9C3B-4E7A-8B5D-1F2E3D4C5B6A}
AppName=Noti
AppVersion={#AppVersion}
AppPublisher=Noti
DefaultDirName={autopf}\Noti
DefaultGroupName=Noti
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
CloseApplications=force
RestartApplications=no
OutputDir={#OutputDir}
OutputBaseFilename=Noti-Setup-win-x64
SetupIconFile=..\Noti.App\Assets\Noti.ico
UninstallDisplayIcon={app}\Noti.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Noti"; Filename: "{app}\Noti.exe"; IconFilename: "{app}\Assets\Noti.ico"
Name: "{autodesktop}\Noti"; Filename: "{app}\Noti.exe"; IconFilename: "{app}\Assets\Noti.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\Noti.exe"; Description: "Launch Noti"; Flags: nowait postinstall skipifsilent
Filename: "{app}\Noti.exe"; Flags: nowait skipifnotsilent
