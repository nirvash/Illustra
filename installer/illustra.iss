#pragma verboselevel 9
#define MyAppName "Illustra"
#define MyAppExeName MyAppName + ".exe"
#define MyAppPath="../publish/" + MyAppExeName
#define MyAppVersion GetVersionNumbersString(MyAppPath)
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0.0"
#endif

#define LICENSE_PATH GetEnv('LICENSE_PATH')
#ifndef LICENSE_PATH
  #define LICENSE_PATH "..\\LICENSE"
#endif

#if FileExists('..\LICENSE')
  #define LicenseFile "..\LICENSE"
#elif FileExists('LICENSE')
  #define LicenseFile "LICENSE"
#else
  #define LicenseFile "LICENSE.txt"
#endif

#define MyAppPublisher "nirvash"
#define MyAppURL "https://github.com/nirvash/Illustra"

[Setup]
; NOTE: The value of AppId uniquely identifies this application.
; Do not use the same AppId value in installers for other applications.
; (To generate a new GUID, click Tools | Generate GUID inside the IDE.)
AppId={{F58425D6-4492-4E9B-9F50-C6EF04D137B8}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
;DefaultDirName={pf}\{#MyAppName}
DefaultDirName={localappdata}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile={#LicenseFile}
OutputBaseFilename=Illustra_installer
Compression=lzma
SolidCompression=yes
PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MyAppPath}"; DestDir: "{app}"; Flags: ignoreversion
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[ThirdParty]
UseRelativePaths=True
