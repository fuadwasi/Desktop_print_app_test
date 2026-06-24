; ============================================================
; Inno Setup Script — PrintDesktopClient Installer
; Version: 1.2.0
; Author: Fuad Hasan
; ============================================================

#define AppName "Print Desktop Client"
#define AppVersion "1.2.0"
#define AppPublisher "Fuad Hasan"
#define AppPublisherURL "https://github.com/fuadwasi"
#define AppSupportEmail "fhassanwasi@gmail.com"
#define AppExeName "PrintDesktopClient.exe"
#define SourceDir "..\bin\Release\net48\publish"
#define OutputDir "Output"

[Setup]
; --- App Identity ---
AppId={{A3F8C2D1-5B7E-4F9A-8C3D-2E1F6A4B7C9D}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} v{#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppPublisherURL}
AppSupportURL={#AppPublisherURL}
AppUpdatesURL={#AppPublisherURL}
AppContact={#AppSupportEmail}

; --- Install Location: Program Files (x86) (standard machine-wide install) ---
DefaultDirName={commonpf32}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes

; --- Privileges: admin required for Program Files installation ---
; Note: UAC prompt will appear during install. The app itself runs as a normal user.
PrivilegesRequired=admin

; --- Output ---
OutputDir={#OutputDir}
OutputBaseFilename=PrintDesktopClient_Setup_v{#AppVersion}

; --- Appearance ---
SetupIconFile=..\Resources\Images\PDC_Icons\favicon.ico
WizardStyle=modern
WizardSmallImageFile=..\Resources\Images\PDC_32x32.png

; --- Compression ---
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

; --- Misc ---
ShowLanguageDialog=no
MinVersion=6.1sp1
ArchitecturesInstallIn64BitMode=x64compatible
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Installer
VersionInfoCopyright=Copyright 2026 {#AppPublisher}
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; User-selectable options during install
Name: "desktopicon";     Description: "Create a &Desktop shortcut";         GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "autostartup";    Description: "Start automatically with &Windows (recommended)"; GroupDescription: "Startup options:"; Flags: checkedonce

[Files]
; Include everything from the publish output folder
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; .NET Framework 4.8 Web Bootstrapper — embedded as a prerequisite.
; It is extracted to a temp folder and run silently if .NET 4.8 is not present.
Source: "Prerequisites\ndp48-web.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall dontcopy

[Icons]
; Start Menu shortcut
Name: "{group}\{#AppName}";          Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

; Desktop shortcut (only if user selected it)
Name: "{autodesktop}\{#AppName}";    Filename: "{app}\{#AppExeName}"; Tasks: desktopicon; IconFilename: "{app}\{#AppExeName}"

[Registry]
; Write auto-startup entry to HKCU (no admin needed).
; Launches with --background so it starts silently to the system tray.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "{#AppName}"; \
  ValueData: """{app}\{#AppExeName}"" --background"; \
  Flags: uninsdeletevalue; Tasks: autostartup

[Run]
; Offer to launch the app immediately after installation finishes
Filename: "{app}\{#AppExeName}"; \
  Description: "Launch {#AppName} now"; \
  Flags: nowait postinstall skipifsilent

[UninstallRun]
; Gracefully shut down the running instance before uninstall
Filename: "taskkill.exe"; Parameters: "/F /IM ""{#AppExeName}"""; Flags: skipifdoesntexist runhidden

[Code]
// ---------------------------------------------------------------------------
// .NET Framework 4.8 prerequisite check and silent auto-install.
// The web bootstrapper (ndp48-web.exe) is embedded in the installer and
// extracted + run silently if .NET 4.8 is not already present on the machine.
// On Windows 10 (1903+) and Windows 11 it is always pre-installed, so this
// code path will almost never execute in practice.
// ---------------------------------------------------------------------------
function IsDotNetFramework48Installed(): Boolean;
var
  InstallKey: String;
  ReleaseValue: Cardinal;
begin
  InstallKey := 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full';
  Result := RegQueryDWordValue(HKLM, InstallKey, 'Release', ReleaseValue) and (ReleaseValue >= 528040);
end;

function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
  BootstrapperPath: String;
begin
  Result := True;

  if not IsDotNetFramework48Installed() then
  begin
    // Inform the user and offer to install automatically
    if MsgBox(
      '.NET Framework 4.8 is required to run ' + '{#AppName}' + '.' + #13#10#13#10 +
      'The installer will now download and install .NET Framework 4.8 from Microsoft.' + #13#10 +
      'An internet connection is required for this step.' + #13#10#13#10 +
      'Click OK to continue, or Cancel to abort.',
      mbConfirmation, MB_OKCANCEL) = IDOK then
    begin
      // Extract the embedded bootstrapper from the installer archive to the temp dir
      ExtractTemporaryFile('ndp48-web.exe');
      BootstrapperPath := ExpandConstant('{tmp}\ndp48-web.exe');

      // Run the bootstrapper silently (/q = quiet, /norestart = no reboot prompt)
      if not Exec(BootstrapperPath, '/q /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
      begin
        MsgBox(
          'Failed to launch the .NET Framework installer.' + #13#10 +
          'Please install .NET Framework 4.8 manually from:' + #13#10 +
          'https://dotnet.microsoft.com/download/dotnet-framework/net48',
          mbError, MB_OK);
        Result := False;
        Exit;
      end;

      // Re-check after installation
      if not IsDotNetFramework48Installed() then
      begin
        MsgBox(
          '.NET Framework 4.8 installation did not complete successfully.' + #13#10 +
          'Please install it manually and re-run this installer.',
          mbError, MB_OK);
        Result := False;
      end;
    end
    else
    begin
      // User cancelled
      Result := False;
    end;
  end;
end;
