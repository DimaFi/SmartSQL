; Compile only through Build-Installer.ps1 after a verified self-contained publication.
#ifndef PublishRoot
  #error PublishRoot must identify the published app directory
#endif
#ifndef BuildVersion
  #error BuildVersion must identify the application version
#endif
#ifndef BuildNumericVersion
  #error BuildNumericVersion must identify the numeric Windows version resource
#endif
#ifndef InstallerOutput
  #error InstallerOutput must identify a new output directory on D:
#endif
#define AppExecutable "SmartSQL.Desktop.exe"

[Setup]
; Keep this AppId stable across versions so upgrades find the existing per-user install.
AppId={{C7D38241-BB69-4A0B-A445-219590EFA587}
AppName=SmartSQL
AppVersion={#BuildVersion}
VersionInfoVersion={#BuildNumericVersion}
AppPublisher=SmartSQL
DefaultDirName=D:\SmartSQL-app\{username}
DefaultGroupName=SmartSQL
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
CloseApplications=yes
RestartApplications=no
SetupIconFile={#PublishRoot}\SmartSQL.ico
UninstallDisplayIcon={app}\{#AppExecutable}
OutputDir={#InstallerOutput}
OutputBaseFilename=SmartSQL-{#BuildVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
; This payload contains binaries only. Never install or remove runtime data/backups/profiles.
Source: "{#PublishRoot}\*"; DestDir: "{app}"; Excludes: "data\*,run-data\*,.workspace\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SmartSQL"; Filename: "{app}\{#AppExecutable}"; WorkingDir: "{app}"
Name: "{autodesktop}\SmartSQL"; Filename: "{app}\{#AppExecutable}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExecutable}"; Description: "Launch SmartSQL"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
; Official Win32/Inno APIs: GetFileAttributesW returns the link's own attributes;
; DLLGetLastError preserves the error from this external call.
function DeliveryGetFileAttributes(const FileName: String): LongWord;
  external 'GetFileAttributesW@kernel32.dll stdcall';

function ValidateInstallDirectory: String;
var
  Directory, Parent: String;
  Attributes: LongWord;
  AttributeError: LongInt;
begin
  Result := '';
  Directory := ExpandFileName(ExpandConstant('{app}'));
  if (Length(Directory) < 4) or (CompareText(Copy(Directory, 1, 3), 'D:\') <> 0) then
  begin
    Result := 'Choose a writable application directory on D: to keep data and temporary files on D:.';
    Exit;
  end;
  while Directory <> '' do
  begin
    Attributes := DeliveryGetFileAttributes(Directory);
    if Attributes = $FFFFFFFF then
    begin
      AttributeError := DLLGetLastError;
      if (AttributeError <> 2) and (AttributeError <> 3) then
      begin
        Result := 'Cannot safely inspect the installation path. Choose a different writable directory on D:.';
        Exit;
      end;
    end
    else if (Attributes and $00000400) <> 0 then
    begin
      Result := 'Junctions and other reparse points are not accepted in the installation path.';
      Exit;
    end;
    if FileExists(AddBackslash(Directory) + 'SmartSQL.sln') then
    begin
      Result := 'Choose a directory outside the SmartSQL source workspace to keep development data separate.';
      Exit;
    end;
    Parent := ExtractFileDir(Directory);
    if Parent = Directory then Break;
    Directory := Parent;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := ValidateInstallDirectory;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ValidationError: String;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    ValidationError := ValidateInstallDirectory;
    if ValidationError <> '' then
    begin
      MsgBox(ValidationError, mbError, MB_OK);
      Result := False;
    end;
  end;
end;
; No [UninstallDelete]: files created by users/application remain intact on upgrade/uninstall.
