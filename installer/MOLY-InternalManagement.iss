; MOLY Internal Management - Windows installer
; Build with Inno Setup 6: ISCC.exe installer\MOLY-InternalManagement.iss

#define MyAppName "MOLY Internal Management"
#define MyAppVersion "2.5.0"
#define MyAppPublisher "MOLY Studio"
#define MyAppExeName "InternalManagement.Desktop.exe"

[Setup]
AppId={{B7A4F0B3-7D1C-4B2D-9B7A-2C54C5D8E1A6}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://www.molystudio.online/admin
DefaultDirName={localappdata}\Programs\MOLY Internal Management
DefaultGroupName=MOLY Internal Management
DisableProgramGroupPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=MOLY-InternalManagement-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
WizardImageFile=moly-wizard.bmp
WizardSmallImageFile=moly-wizard.bmp
WizardImageStretch=yes
CloseApplications=yes
RestartApplications=no
SetupIconFile=app-icon.ico
UninstallDisplayName={#MyAppName}
UninstallIconFile={app}\Assets\app-icon.ico
Uninstallable=yes
LicenseFile=MOLY-License.txt
SetupLogging=yes

[Tasks]
Name: "desktopicon"; Description: "Tạo shortcut trên màn hình Desktop"; GroupDescription: "Shortcut bổ sung:"; Flags: unchecked

[Files]
Source: "..\artifacts\Moli-Release-HourlyPayroll\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Khởi chạy {#MyAppName}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
