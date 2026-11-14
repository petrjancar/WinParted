; WinParted installer (Inno Setup 6). Design D2 of the add-installer-and-updates change.
;
; Build from the repository root after `dotnet publish` (see README, "Releasing"):
;   iscc /DAppVersion=0.2.0 installer\WinParted.iss
; Output: artifacts\WinParted-<version>-setup.exe

#ifndef AppVersion
  #error Pass the version: iscc /DAppVersion=x.y.z installer\WinParted.iss
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\WinParted-win-x64"
#endif

; Identifies the installation for upgrades and uninstall. Never change it.
#define AppGuid "51C87A5F-8550-4444-B119-9C37A72BC9D6"

[Setup]
AppId={{{#AppGuid}}
AppName=WinParted
AppVersion={#AppVersion}
AppVerName=WinParted {#AppVersion}
AppPublisher=WinParted contributors
AppPublisherURL=https://github.com/petrjancar/WinParted
AppSupportURL=https://github.com/petrjancar/WinParted/issues
AppUpdatesURL=https://github.com/petrjancar/WinParted/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\WinParted
DefaultGroupName=WinParted
DisableProgramGroupPage=yes
; Installed for all users; Windows 11 (build 22000) x64 only. Older Windows or other CPUs get
; Inno Setup's standard "requires" message and nothing is installed.
PrivilegesRequired=admin
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
; WinParted holds these mutexes while it runs: install and uninstall ask the user to close it first.
AppMutex=WinParted.Running,Global\WinParted.Running
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=WinParted
UninstallDisplayIcon={app}\WinParted.exe
SetupIconFile=..\src\WinParted.App\Assets\WinParted.ico
; {localappdata} below is the 0.1.0 log folder of the account running the uninstaller (installation spec).
UsedUserAreasWarning=no
OutputDir=..\artifacts
OutputBaseFilename=WinParted-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\WinParted"; Filename: "{app}\WinParted.exe"
Name: "{group}\Uninstall WinParted"; Filename: "{uninstallexe}"

[Run]
; Setup is already elevated, so WinParted starts without a second prompt.
Filename: "{app}\WinParted.exe"; Description: "Launch WinParted"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallDelete]
; Everything WinParted creates at runtime (installation spec, "Uninstall leaves no files").
Type: filesandordirs; Name: "{commonappdata}\WinParted"
Type: filesandordirs; Name: "{localappdata}\WinParted"

[Code]
{ Parses "1.2.3" into three numbers; missing or invalid parts count as 0. }
function VersionPart(Version: String; Index: Integer): Integer;
var
  I: Integer;
begin
  for I := 1 to Index do
  begin
    if Pos('.', Version) > 0 then
      Delete(Version, 1, Pos('.', Version))
    else
      Version := '';
  end;
  if Pos('.', Version) > 0 then
    Version := Copy(Version, 1, Pos('.', Version) - 1);
  Result := StrToIntDef(Version, 0);
end;

{ > 0 when A is newer than B. }
function CompareVersion(A, B: String): Integer;
var
  I: Integer;
begin
  Result := 0;
  for I := 0 to 2 do
  begin
    Result := VersionPart(A, I) - VersionPart(B, I);
    if Result <> 0 then
      Exit;
  end;
end;

{ Installing an older version over a newer one asks first (installation spec). Same version = repair. }
function InitializeSetup(): Boolean;
var
  Installed: String;
begin
  Result := True;
  if RegQueryStringValue(HKLM64, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{{#AppGuid}}_is1', 'DisplayVersion', Installed)
    and (CompareVersion(Installed, '{#AppVersion}') > 0) then
  begin
    Result := SuppressibleMsgBox(
      'A newer version of WinParted (' + Installed + ') is installed.' + #13#10#13#10 +
      'Install the older version {#AppVersion} anyway?',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES;
  end;
end;
