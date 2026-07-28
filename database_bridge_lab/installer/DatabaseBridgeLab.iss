; Almutamakkin Database Bridge Lab — Inno Setup installer
; Builds a Windows x64 setup that installs the self-contained publish output.

#define MyAppName "جسر المتمكن"
#define MyAppNameEn "Almutamakkin Bridge"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.15"
#endif
#define MyAppPublisher "Almutamakkin"
#define MyAppExeName "Almutamakkin.DatabaseBridgeLab.exe"
#define MyAppId "{{A8F3C2E1-9B47-4D6A-8F12-5C7E9A1B3D40}"

#ifndef SourceDir
  #define SourceDir "..\publish\win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Almutamakkin\DatabaseBridgeLab
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=Almutamakkin-DatabaseBridgeLab-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppNameEn}
SetupLogging=yes
CloseApplications=yes
RestartApplications=no
MinVersion=10.0

[Languages]
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,createdump.exe"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\إلغاء التثبيت"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Keep user settings/logs under LocalAppData; remove only empty install leftovers.
Type: filesandordirs; Name: "{app}\*.pdb"
Type: dirifempty; Name: "{app}"

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;
