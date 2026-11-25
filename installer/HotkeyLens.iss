; HotkeyLens installer.
; Build with packaging\build-release.ps1 so the version and compiler path are resolved consistently.

#ifndef MyAppVersion
  #define MyAppVersion "1.1.1"
#endif
#ifndef MyAppPublisher
  #define MyAppPublisher "mengzi0771"
#endif
#ifndef MyOutputBaseFilename
  #define MyOutputBaseFilename "HotkeyLens-Setup"
#endif

#define MyAppName "HotkeyLens"
#define MyAppExeName "HotkeyLens.exe"
#define MyAppUrl "https://github.com/mengzi0771/HotkeyLens"

[Setup]
; Keep this AppId stable: it is how Windows and Inno Setup recognize upgrades.
AppId={{26F1B708-3044-44E6-BA58-9D3BD08AB3D9}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
AppComments=Discover Every Shortcut. Windows shortcut discovery, provenance and conflict analysis.
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
InfoBeforeFile=..\packaging\INSTALL-NOTICE.txt
OutputDir=..\artifacts\dist
OutputBaseFilename={#MyOutputBaseFilename}
SetupIconFile=..\src\HotkeyLens.App\Assets\HotkeyLens.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
CloseApplicationsFilter=HotkeyLens.exe
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousTasks=yes
ChangesAssociations=no
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=HotkeyLens Setup
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCopyright=Copyright (C) 2026 {#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "Discover Every Shortcut"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "Discover Every Shortcut"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

; Deliberately no [UninstallDelete] for user data. HotkeyLens stores data in
; %LOCALAPPDATA%\HotkeyLens, outside {app}, and upgrades/uninstall must preserve it.

[Code]
// Fail early with an actionable message when the install directory cannot be
// written. Inno Setup still owns the actual file replacement and rollback.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ProbePath: String;
begin
  Result := '';
  if not DirExists(ExpandConstant('{app}')) then
    Exit;

  ProbePath := ExpandConstant('{app}\.hotkeylens-write-probe.tmp');
  if not SaveStringToFile(ProbePath, 'probe', False) then
  begin
    Result := '无法写入安装目录。请检查目录权限或安全软件主动防御设置；' +
              'HotkeyLens 不会关闭或绕过安全软件。当前版本和用户数据不会被修改。';
    Exit;
  end;

  DeleteFile(ProbePath);
end;