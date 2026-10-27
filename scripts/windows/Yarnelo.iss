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
DefaultDirName={autopf}\Yarnelo
DisableProgramGroupPage=yes
DefaultGroupName=Yarnelo
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
; Reuse both the scope and directory of an existing installation. In
; particular, never install a second copy beside an existing portable profile.
UsePreviousPrivileges=yes
UsePreviousAppDir=yes
#if AppArchitecture == "arm64"
MinVersion=10.0.22000
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#elif AppArchitecture == "x64"
MinVersion=10.0
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
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
; A portable ZIP and Setup share a clean payload, but Setup must not switch a
; standard AppData profile to exe\Data. Leave an EXISTING marker untouched so
; older installations keep their own profile and absolute library paths.
Source: "{#Payload}\*"; DestDir: "{app}"; Excludes: "portable.mode"; Flags: ignoreversion
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
english.RestoreFailed=Yarnelo could not return every moved shortcut to the desktop. Uninstall was stopped before removing the program. Your profile and shortcut library are retained. Check desktop access and try again.
russian.RestoreFailed=Yarnelo не удалось вернуть все перемещённые ярлыки на рабочий стол. Удаление остановлено до удаления программы. Профиль и библиотека ярлыков сохранены. Проверьте доступ к рабочему столу и повторите попытку.
english.ProfileNote=Moved shortcuts have been returned to the desktop. Your settings and shortcut library are retained in the existing profile folder.
russian.ProfileNote=Перемещённые ярлыки возвращены на рабочий стол. Настройки и библиотека ярлыков сохранены в прежней папке профиля.

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

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  { This event is after confirmation and before ANY installed file is removed.
    A fatal exception here aborts Uninstall; returning normally would delete
    the program even if restoring a shortcut failed. }
  if CurUninstallStep = usUninstall then
  begin
    ResultCode := -1;
    if not Exec(ExpandConstant('{app}\Yarnelo.SetupMaintenance.exe'),
        'uninstall "' + ExpandConstant('{app}\Yarnelo.exe') + '"', '', SW_HIDE,
        ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    begin
      Log(Format('Yarnelo uninstall preparation failed (%d).', [ResultCode]));
      if ResultCode = 50 then
        RaiseException(CustomMessage('CloseFailed'))
      else
        RaiseException(CustomMessage('RestoreFailed'));
    end;
  end;
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    MsgBox(CustomMessage('ProfileNote'), mbInformation, MB_OK);
end;
