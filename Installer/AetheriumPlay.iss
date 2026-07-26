; Aetherium Play all-in-one player setup
; Developed by Vanquish, aka Chosen One

#define MyAppName "Aetherium Play"
#ifndef MyAppVersion
#define MyAppVersion "1.0.10"
#endif
#define MyAppPublisher "Vanquish (aka Chosen One)"
#define MyAppExeName "AetheriumLauncher.exe"
#define PublishDir "..\AetheriumLauncher\bin\Release\net8.0-windows\win-x86\publish"

[Setup]
AppId={{D235F19A-681F-4A65-9AC5-9FD3DB8C4D9F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Aetherium Play
DisableDirPage=auto
DisableProgramGroupPage=yes
DisableWelcomePage=yes
ArchitecturesAllowed=x86compatible
#ifdef TestBuild
PrivilegesRequired=lowest
#else
PrivilegesRequired=admin
#endif
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=ASHERON.ICO
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=..\artifacts\installer
OutputBaseFilename=AetheriumPlaySetup
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Aetherium Play Setup
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
LicenseFile=AetheriumPlayAgreement.txt
InfoBeforeFile=AetheriumPlaySources.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "skin\default"; Description: "&Default"; GroupDescription: "Launcher skin:"; Flags: exclusive
Name: "skin\pk"; Description: "&PK"; GroupDescription: "Launcher skin:"; Flags: exclusive unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "AetheriumPlayAgreement.txt"; DestDir: "{app}\Documentation"; Flags: ignoreversion
Source: "AetheriumPlaySources.txt"; DestDir: "{app}\Documentation"; Flags: ignoreversion
Source: "Patch-SetupInxIeCheck.ps1"; DestDir: "{app}\Bootstrap"; Flags: ignoreversion
Source: "..\ThirdParty\MegaApiClient\LICENSE"; DestDir: "{app}\Documentation"; DestName: "MegaApiClient-LICENSE.txt"; Flags: ignoreversion
Source: "..\tools\dgvoodoo\NOTICE.md"; DestDir: "{app}\Documentation"; DestName: "dgVoodoo-NOTICE.md"; Flags: ignoreversion
Source: "..\tools\dgvoodoo\extracted\MS\x86\DDraw.dll"; DestDir: "{app}\dgvoodoo\extracted\MS\x86"; Flags: ignoreversion
Source: "..\tools\dgvoodoo\extracted\MS\x86\D3DImm.dll"; DestDir: "{app}\dgvoodoo\extracted\MS\x86"; Flags: ignoreversion
Source: "..\tools\dgvoodoo\extracted\dgVoodoo.conf"; DestDir: "{app}\dgvoodoo\extracted"; Flags: ignoreversion

[Registry]
#ifndef TestBuild
Root: HKLM; Subkey: "SOFTWARE\WOW6432Node\Microsoft\Microsoft Games\Asheron's Call\1.00"; ValueType: dword; ValueName: "UseHardware"; ValueData: "1"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\WOW6432Node\Microsoft\Microsoft Games\Asheron's Call\1.00"; ValueType: dword; ValueName: "DoubleBuffer"; ValueData: "2"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\WOW6432Node\Microsoft\Microsoft Games\Asheron's Call\1.00"; ValueType: dword; ValueName: "FullScreen"; ValueData: "1"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\WOW6432Node\Microsoft\Microsoft Games\Asheron's Call\1.00"; ValueType: dword; ValueName: "ZBuffer2"; ValueData: "0"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\WOW6432Node\Microsoft\Microsoft Games\Asheron's Call\1.00"; ValueType: dword; ValueName: "ScreenWidth"; ValueData: "800"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\WOW6432Node\Microsoft\Microsoft Games\Asheron's Call\1.00"; ValueType: dword; ValueName: "ScreenHeight"; ValueData: "600"; Flags: uninsdeletevalue
#endif

[Icons]
Name: "{autoprograms}\Aetherium Play"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Aetherium Play"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--prepare-community-game-installer ""{commonappdata}\AetheriumPlay\Bootstrap"""; StatusMsg: "Downloading, verifying, and preparing the original Dark Majesty installer..."; Check: IsLegacyGameInstallRequired; AfterInstall: PatchLegacyInstallerPayload; Flags: waituntilterminated
Filename: "{commonappdata}\AetheriumPlay\Bootstrap\legacy\Disk1\setup.exe"; WorkingDir: "{commonappdata}\AetheriumPlay\Bootstrap\legacy\Disk1"; StatusMsg: "Complete the original Dark Majesty installation wizard..."; Check: IsLegacyGameInstallRequired; AfterInstall: RequireGameInstall; Flags: waituntilterminated
Filename: "{sys}\taskkill.exe"; Parameters: "/F /T /IM aclauncher.exe"; StatusMsg: "Closing the obsolete original launcher..."; Check: IsLegacyGameInstallRequired; Flags: runhidden waituntilterminated

Filename: "{app}\{#MyAppExeName}"; Parameters: "--install-community-client-from-file ""{param:COMMUNITYCLIENTFILE|}"" ""{code:GetGameInstallDir}"""; StatusMsg: "Installing and verifying the Dark Majesty client..."; Check: HasCommunityClientFile; AfterInstall: VerifyCommunityClient; Flags: waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Parameters: "--install-community-client-with-progress ""{code:GetGameInstallDir}"""; StatusMsg: "Downloading and verifying the Dark Majesty client..."; Check: not HasCommunityClientFile; AfterInstall: VerifyCommunityClient; Flags: waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Parameters: "--configure-aetherium-install ""{code:GetGameInstallDir}"" ""{code:GetSelectedSkin}"""; StatusMsg: "Configuring Aetherium Play for play.aetherium.ac:9000..."; AfterInstall: VerifyLauncherConfiguration; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Aetherium Play"; Flags: nowait postinstall skipifsilent

[Code]
var
  LegacyGameInstallRequired: Boolean;
  ResolvedGameInstallDir: string;

procedure RunRequired(
  const FileName, Parameters, WorkingDirectory, FailureMessage: string;
  ShowCommand: Integer);
var
  ResultCode: Integer;
begin
  if (not Exec(
    FileName,
    Parameters,
    WorkingDirectory,
    ShowCommand,
    ewWaitUntilTerminated,
    ResultCode)) or (ResultCode <> 0) then
  begin
    RaiseException(FailureMessage + #13#10 + #13#10 +
      'See: ' +
      ExpandConstant('{commonappdata}\AetheriumPlay\Logs\setup.log'));
  end;
end;

function HasCommunityClientFile: Boolean;
begin
  Result := ExpandConstant('{param:COMMUNITYCLIENTFILE|}') <> '';
end;

function IsCompleteGameDirectory(const DirectoryName: string): Boolean;
begin
  Result :=
    (DirectoryName <> '') and
    FileExists(AddBackslash(DirectoryName) + 'client.exe') and
    FileExists(AddBackslash(DirectoryName) + 'portal.dat') and
    FileExists(AddBackslash(DirectoryName) + 'cell.dat');
end;

function FindClientExeSubfolder(BaseDir: string; var FoundDir: string): Boolean;
var
  FindRec: TFindRec;
  Candidate: string;
begin
  Result := False;
  if not DirExists(BaseDir) then
    Exit;

  if FindFirst(AddBackslash(BaseDir) + '*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0) and
           (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          Candidate := AddBackslash(BaseDir) + FindRec.Name;
          if IsCompleteGameDirectory(Candidate) then
          begin
            FoundDir := Candidate;
            Result := True;
            Exit;
          end;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function CleanUninstallString(const S: string): string;
var
  SpacePos: Integer;
begin
  Result := Trim(S);
  if (Length(Result) > 0) and (Result[1] = '"') then
  begin
    Result := Copy(Result, 2, Length(Result) - 1);
    if Pos('"', Result) > 0 then
      Result := Copy(Result, 1, Pos('"', Result) - 1);
  end
  else
  begin
    SpacePos := Pos(' ', Result);
    if SpacePos > 0 then
      Result := Copy(Result, 1, SpacePos - 1);
  end;
end;

function TryGetInstallDirFromUninstallRegistry(var FoundDir: string): Boolean;
var
  Roots: array[0..1] of string;
  RootIdx, Index: Integer;
  SubKeys: TArrayOfString;
  KeyPath, DisplayName, InstallLocation, UninstallString, Candidate: string;
begin
  Result := False;
  Roots[0] := 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall';
  Roots[1] := 'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall';

  for RootIdx := 0 to 1 do
  begin
    if not RegGetSubkeyNames(HKLM, Roots[RootIdx], SubKeys) then
      Continue;

    for Index := 0 to GetArrayLength(SubKeys) - 1 do
    begin
      KeyPath := Roots[RootIdx] + '\' + SubKeys[Index];
      if not RegQueryStringValue(HKLM, KeyPath, 'DisplayName', DisplayName) then
        Continue;
      if Pos('asheron', Lowercase(DisplayName)) = 0 then
        Continue;

      if RegQueryStringValue(HKLM, KeyPath, 'InstallLocation', InstallLocation) then
      begin
        Candidate := Trim(InstallLocation);
        if IsCompleteGameDirectory(Candidate) then
        begin
          FoundDir := Candidate;
          Result := True;
          Exit;
        end;
      end;

      if RegQueryStringValue(HKLM, KeyPath, 'UninstallString', UninstallString) then
      begin
        Candidate := ExtractFileDir(CleanUninstallString(UninstallString));
        if IsCompleteGameDirectory(Candidate) then
        begin
          FoundDir := Candidate;
          Result := True;
          Exit;
        end;
      end;
    end;
  end;
end;

function SearchShortcutsRecursive(const Folder: string; var FoundDir: string): Boolean;
var
  FindRec: TFindRec;
  Shell: Variant;
  ShortcutPath, TargetPath, Candidate: string;
begin
  Result := False;
  if not DirExists(Folder) then
    Exit;

  if FindFirst(AddBackslash(Folder) + '*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Name = '.') or (FindRec.Name = '..') then
          Continue;

        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          if SearchShortcutsRecursive(AddBackslash(Folder) + FindRec.Name, FoundDir) then
          begin
            Result := True;
            Exit;
          end;
        end
        else if (Lowercase(ExtractFileExt(FindRec.Name)) = '.lnk') and
                (Pos('asheron', Lowercase(FindRec.Name)) > 0) then
        begin
          ShortcutPath := AddBackslash(Folder) + FindRec.Name;
          try
            Shell := CreateOleObject('WScript.Shell');
            TargetPath := Shell.CreateShortcut(ShortcutPath).TargetPath;
            Candidate := ExtractFileDir(TargetPath);
            if IsCompleteGameDirectory(Candidate) then
            begin
              FoundDir := Candidate;
              Result := True;
              Exit;
            end;
          except
          end;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function TryResolveGameInstallDir(var FoundDir: string): Boolean;
var
  OverrideDir: string;
begin
  OverrideDir := ExpandConstant('{param:GAMEINSTALLDIR|}');
  if IsCompleteGameDirectory(OverrideDir) then
  begin
    FoundDir := OverrideDir;
    Result := True;
    Exit;
  end;

  if IsCompleteGameDirectory('C:\asheronscalldm') then
  begin
    FoundDir := 'C:\asheronscalldm';
    Result := True;
    Exit;
  end;

  if FindClientExeSubfolder('C:\Turbine Entertainment Software', FoundDir) or
     FindClientExeSubfolder('C:\Turbine', FoundDir) or
     FindClientExeSubfolder(
       ExpandConstant('{pf32}\Turbine Entertainment Software'), FoundDir) or
     FindClientExeSubfolder(ExpandConstant('{pf32}\Turbine'), FoundDir) or
     FindClientExeSubfolder(ExpandConstant('{pf32}\Microsoft Games'), FoundDir) or
     TryGetInstallDirFromUninstallRegistry(FoundDir) or
     SearchShortcutsRecursive(ExpandConstant('{commonprograms}'), FoundDir) or
     SearchShortcutsRecursive(ExpandConstant('{userprograms}'), FoundDir) or
     SearchShortcutsRecursive(ExpandConstant('{commondesktop}'), FoundDir) or
     SearchShortcutsRecursive(ExpandConstant('{userdesktop}'), FoundDir) then
  begin
    Result := True;
    Exit;
  end;

  Result := False;
end;

procedure InitializeWizard();
begin
  LegacyGameInstallRequired := not TryResolveGameInstallDir(ResolvedGameInstallDir);
end;

function IsLegacyGameInstallRequired: Boolean;
begin
  Result := LegacyGameInstallRequired;
end;

procedure PatchLegacyInstallerPayload();
var
  SetupInxPath: string;
begin
  SetupInxPath := ExpandConstant(
    '{commonappdata}\AetheriumPlay\Bootstrap\legacy\Disk1\setup.inx');
  if not FileExists(SetupInxPath) then
    RaiseException('The original installer did not extract its Disk1 payload.');

  RunRequired(
    ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -ExecutionPolicy Bypass -File "' +
      ExpandConstant('{app}\Bootstrap\Patch-SetupInxIeCheck.ps1') +
      '" -Path "' + SetupInxPath + '"',
    ExtractFileDir(SetupInxPath),
    'The obsolete Internet Explorer check could not be patched.',
    SW_HIDE);
end;

procedure RequireGameInstall();
var
  SelectedDirectory: string;
begin
  if TryResolveGameInstallDir(ResolvedGameInstallDir) then
    Exit;

  SelectedDirectory := ExpandConstant('{pf32}\Turbine\Asheron''s Call');
  if BrowseForFolder(
       'Select the Dark Majesty folder you just installed. It contains client.exe.',
       SelectedDirectory,
       False) and IsCompleteGameDirectory(SelectedDirectory) then
  begin
    ResolvedGameInstallDir := SelectedDirectory;
    Exit;
  end;

  RaiseException(
    'Aetherium Play could not find a complete Dark Majesty installation. ' +
    'Run setup again and select the folder containing client.exe, portal.dat, ' +
    'and cell.dat when prompted.');
end;

function GetGameInstallDir(Param: string): string;
begin
  if not IsCompleteGameDirectory(ResolvedGameInstallDir) then
    RequireGameInstall();
  Result := ResolvedGameInstallDir;
end;

function GetSelectedSkin(Param: string): string;
begin
  Result := 'default';
  if WizardIsTaskSelected('skin\pk') then
    Result := 'pk';
end;

procedure VerifyCommunityClient();
var
  ResultCode: Integer;
begin
  if (not Exec(
    ExpandConstant('{app}\{#MyAppExeName}'),
    '--verify-community-client "' + GetGameInstallDir('') + '"',
    ExpandConstant('{app}'),
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode)) or (ResultCode <> 0) then
  begin
    RaiseException(
      'The Dark Majesty client download or verification did not complete. ' +
      'The existing client.exe was left unchanged.' + #13#10 + #13#10 +
      'See: ' +
      ExpandConstant('{commonappdata}\AetheriumPlay\Logs\setup.log'));
  end;
end;

procedure VerifyLauncherConfiguration();
begin
  if not FileExists(ExpandConstant('{app}\game.install.path')) then
    RaiseException(
      'Aetherium Launcher could not save the selected game location.' +
      '' + #13#10 + #13#10 + 'See: ' +
      ExpandConstant('{commonappdata}\AetheriumPlay\Logs\setup.log'));
  if not FileExists(AddBackslash(GetGameInstallDir('')) + 'launcher.json') then
    RaiseException('Aetherium Launcher could not save its play.aetherium.ac configuration.');
end;
