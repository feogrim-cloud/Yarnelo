; Compile using Build-Installer.ps1 with Inno Setup 6.3 or newer.
#ifndef Payload
  #error Payload must point to a freshly generated package directory.
#endif
#if Ver < 0x06030000
  #error Inno Setup 6.3 or newer is required.
#endif
#ifndef OutputDirectory
  #error OutputDirectory must be specified.
#endif
#ifndef AppArchitecture
  #define AppArchitecture "x64"
#endif
#define AppVersion GetFileVersion(Payload + "\Yarnelo.exe")

[Setup]
AppId={{0FD657E9-E4CC-4BD3-8C71-EE8AA91BA43D}
AppName=Yarnelo
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
VersionInfoTextVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
VersionInfoProductTextVersion={#AppVersion}
AppVerName=Yarnelo {#AppVersion} ({#AppArchitecture})
AppPublisher=feogrim-cloud
AppPublisherURL=https://github.com/feogrim-cloud/Yarnelo
AppSupportURL=https://github.com/feogrim-cloud/Yarnelo/issues
AppUpdatesURL=https://github.com/feogrim-cloud/Yarnelo/releases
DefaultDirName={localappdata}\Programs\Yarnelo
DisableProgramGroupPage=yes
DefaultGroupName=Yarnelo
PrivilegesRequired=lowest
#if AppArchitecture == "arm64"
MinVersion=10.0.22000
ArchitecturesAllowed=arm64
#elif AppArchitecture == "x64"
MinVersion=10.0
ArchitecturesAllowed=x64os
#else
MinVersion=10.0
ArchitecturesAllowed=x86compatible
#endif
OutputDir={#OutputDirectory}
OutputBaseFilename=Yarnelo-Windows-{#AppArchitecture}-Setup
SetupIconFile={#Payload}\Yarnelo.ico
UninstallDisplayIcon={app}\Yarnelo.exe
LicenseFile={#Payload}\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
Uninstallable=yes
UninstallFilesDir={app}\Uninstall

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The build script validates this fresh directory and rejects any profile/Data.
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Maintenance}"; DestDir: "{app}"; DestName: "Yarnelo.SetupMaintenance.exe"; Flags: ignoreversion
Source: "{#Maintenance}"; DestName: "Yarnelo.SetupMaintenance.exe"; Flags: dontcopy

[Icons]
Name: "{userprograms}\Yarnelo"; Filename: "{app}\Yarnelo.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Yarnelo.exe"
Name: "{userdesktop}\Yarnelo"; Filename: "{app}\Yarnelo.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Yarnelo.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Yarnelo.exe"; Description: "{cm:LaunchProgram,Yarnelo}"; Flags: nowait postinstall skipifsilent unchecked

[CustomMessages]
#if AppArchitecture == "arm64"
english.DotNetRequired=Yarnelo ARM64 requires Windows 11 ARM64 and Microsoft .NET Framework 4.8.1. Install it from https://dotnet.microsoft.com/download/dotnet-framework/net481 and run Setup again.
russian.DotNetRequired=Для Yarnelo ARM64 нужны Windows 11 ARM64 и Microsoft .NET Framework 4.8.1. Установите его с https://dotnet.microsoft.com/download/dotnet-framework/net481 и запустите установщик снова.
#else
english.DotNetRequired=Yarnelo requires Microsoft .NET Framework 4.8 or 4.8.1. Install it from https://dotnet.microsoft.com/download/dotnet-framework/net48 and run Setup again.
russian.DotNetRequired=Для Yarnelo нужен Microsoft .NET Framework 4.8 или 4.8.1. Установите его с https://dotnet.microsoft.com/download/dotnet-framework/net48 и запустите установщик снова.
#endif
english.CloseFailed=Close Yarnelo using its tray menu and try again. Setup never force-closes applications.
russian.CloseFailed=Закройте Yarnelo через меню в области уведомлений и повторите попытку. Установщик не завершает приложения принудительно.
english.ProfileNote=Your Data folder is retained when updating or uninstalling. Before deleting Data, return imported shortcuts through the dock menu.
russian.ProfileNote=Папка Data сохраняется при обновлении и удалении. Перед удалением Data верните перемещённые ярлыки через меню панели.

[Code]
function InitializeSetup: Boolean;
begin
#if AppArchitecture == "arm64"
  Result := IsDotNetInstalled(net481, 0);
#else
  Result := IsDotNetInstalled(net48, 0);
#endif
  if not Result then
    SuppressibleMsgBox(CustomMessage('DotNetRequired'), mbError, MB_OK, IDOK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  ExtractTemporaryFile('Yarnelo.SetupMaintenance.exe');
  if not Exec(ExpandConstant('{tmp}\Yarnelo.SetupMaintenance.exe'),
      'close "' + ExpandConstant('{app}\Yarnelo.exe') + '"', '', SW_HIDE,
      ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    Result := CustomMessage('CloseFailed');
end;

function InitializeUninstall: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{app}\Yarnelo.SetupMaintenance.exe'),
      'close "' + ExpandConstant('{app}\Yarnelo.exe') + '"', '', SW_HIDE,
      ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
  if not Result then
    SuppressibleMsgBox(CustomMessage('CloseFailed'), mbError, MB_OK, IDOK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  { Change startup only after the user has confirmed the actual uninstall. }
  if CurUninstallStep = usUninstall then
    if not Exec(ExpandConstant('{app}\Yarnelo.SetupMaintenance.exe'),
        'uninstall "' + ExpandConstant('{app}\Yarnelo.exe') + '"', '', SW_HIDE,
        ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
      Log('Yarnelo startup cleanup could not complete.');
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    MsgBox(CustomMessage('ProfileNote'), mbInformation, MB_OK);
end;
