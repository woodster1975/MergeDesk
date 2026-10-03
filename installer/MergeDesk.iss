; Open this file in Inno Setup and choose Build > Compile (Ctrl+F9).
; Keep AppId unchanged for future upgrades.
#ifndef ReleasePath
  #define ReleasePath "..\artifacts\publish\win-x64"
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#if !FileExists(AddBackslash(ReleasePath) + "MergeDesk.App.exe")
  #error ReleasePath must contain the complete published MergeDesk application.
#endif

[Setup]
AppId={{B78E7D4A-40BC-4B8F-AC74-F16E8A5940EF}
AppName=MergeDesk
AppVersion={#AppVersion}
AppPublisher=Paul Woodhouse
AppCopyright=Copyright (c) 2026 Paul Woodhouse
DefaultDirName={autopf}\MergeDesk
DefaultGroupName=MergeDesk
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible and not arm64
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
PrivilegesRequired=admin
LicenseFile=LICENSE.txt
SetupIconFile=MergeDesk.ico
UninstallDisplayIcon={app}\MergeDesk.App.exe
OutputDir=.
OutputBaseFilename=MergeDesk-Setup-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
Uninstallable=yes
VersionInfoVersion={#AppVersion}.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#ReleasePath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\MergeDesk"; Filename: "{app}\MergeDesk.App.exe"; WorkingDir: "{app}"
Name: "{group}\Uninstall MergeDesk"; Filename: "{uninstallexe}"
Name: "{autodesktop}\MergeDesk"; Filename: "{app}\MergeDesk.App.exe"; WorkingDir: "{app}"; Tasks: desktopicon

; No automatic launch, mailbox access or deletion of LocalAppData is performed.
