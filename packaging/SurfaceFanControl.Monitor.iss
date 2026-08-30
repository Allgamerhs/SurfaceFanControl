#ifndef AppVersion
  #error AppVersion command-line define is required
#endif
#ifndef BuildDir
  #error BuildDir command-line define is required
#endif
#ifndef SourceRoot
  #error SourceRoot command-line define is required
#endif
#ifndef OutputDir
  #error OutputDir command-line define is required
#endif

#define AppName "Surface Fan Control"
#define AppExeName "SurfaceProFanControl.exe"

[Setup]
AppId={{D72FE883-3E55-4B63-A0D9-94D5E12B6047}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Surface Fan Control contributors
DefaultDirName={autopf}\SurfaceFanControl
DefaultGroupName={#AppName}
AllowNoIcons=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
ChangesAssociations=no
CloseApplications=yes
Compression=none
DisableProgramGroupPage=yes
LicenseFile={#SourceRoot}\LICENSE
MinVersion=10.0.17763
OutputBaseFilename=SurfaceFanControl-Setup-v{#AppVersion}
OutputDir={#OutputDir}
PrivilegesRequired=admin
SetupIconFile={#SourceRoot}\src\SurfaceProFanControl\Assets\fan.ico
SolidCompression=no
UninstallDisplayIcon={app}\{#AppExeName}
UsePreviousAppDir=yes
WizardStyle=modern

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "Windows ile başlat"; GroupDescription: "Başlangıç seçenekleri"

[Files]
Source: "{#BuildDir}\SurfaceProFanControl.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\SurfaceProFanControl.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}\SurfaceProFanControl.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "{#SourceRoot}\packaging\MONITOR-SETUP-README.txt"; DestDir: "{app}"; DestName: "README.txt"; Flags: ignoreversion

[Registry]
Root: HKLM64; Subkey: "Software\SurfaceFanControl"; ValueType: string; ValueName: "InstallLocation"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKLM64; Subkey: "Software\SurfaceFanControl"; ValueType: string; ValueName: "Version"; ValueData: "{#AppVersion}"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Parameters: "--enable-startup"; Description: "Windows ile başlangıcı ayarla"; Tasks: autostart; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#AppExeName}"; Parameters: "--disable-startup"; Flags: runhidden waituntilterminated; RunOnceId: "DisableStartup"
