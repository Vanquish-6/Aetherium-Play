; Aetherium Play all-in-one player setup
; Developed by Vanquish, aka Chosen One

#define MyAppName "Aetherium Play"
#ifndef MyAppVersion
#define MyAppVersion "1.0.30"
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
Filename: "{app}\{#MyAppExeName}"; Parameters: "--prepare-community-game-installer ""{commonappdata}\AetheriumPlay\Bootstrap"""; StatusMsg: "Downloading the original Dark Majesty installer (about 230 MB). On a slow connection this can take several minutes. Leave this window open..."; Check: IsLegacyGameInstallRequired; AfterInstall: PatchLegacyInstallerPayload; Flags: waituntilterminated
Filename: "{commonappdata}\AetheriumPlay\Bootstrap\legacy\Disk1\setup.exe"; WorkingDir: "{commonappdata}\AetheriumPlay\Bootstrap\legacy\Disk1"; StatusMsg: "Complete the original Dark Majesty installation wizard..."; Check: IsLegacyGameInstallRequired; BeforeInstall: ExplainLegacyInstaller; AfterInstall: WaitForLegacyInstaller; Flags: waituntilterminated
Filename: "{sys}\taskkill.exe"; Parameters: "/F /T /IM aclauncher.exe"; StatusMsg: "Closing the obsolete original launcher..."; Check: IsLegacyGameInstallRequired; Flags: runhidden waituntilterminated

Filename: "{app}\{#MyAppExeName}"; Parameters: "--install-community-client-from-file ""{param:COMMUNITYCLIENTFILE|}"" ""{code:GetGameInstallDir}"""; StatusMsg: "Installing and verifying the Dark Majesty client..."; Check: HasCommunityClientFile; BeforeInstall: EnsureGameInstallDir; AfterInstall: VerifyCommunityClient; Flags: waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Parameters: "--install-community-client-with-progress ""{code:GetGameInstallDir}"""; StatusMsg: "Downloading and verifying the Dark Majesty client. On a slow connection this can take a few minutes..."; Check: not HasCommunityClientFile; BeforeInstall: EnsureGameInstallDir; AfterInstall: VerifyCommunityClient; Flags: waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Parameters: "--configure-aetherium-install ""{code:GetGameInstallDir}"" ""{code:GetSelectedSkin}"""; StatusMsg: "Configuring Aetherium Play for play.aetherium.ac:9000..."; BeforeInstall: EnsureGameInstallDir; AfterInstall: VerifyLauncherConfiguration; Flags: runhidden waituntilterminated
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

procedure LogSetup(const Msg: string);
var
  LogPath: string;
begin
  LogPath := ExpandConstant('{commonappdata}\AetheriumPlay\Logs\setup.log');
  ForceDirectories(ExtractFileDir(LogPath));
  SaveStringToFile(
    LogPath,
    '[' + GetDateTimeString('yyyy-mm-dd hh:nn:ss', #0, #0) + '] ' + Msg + #13#10,
    True);
end;

function IsCompleteGameDirectory(const DirectoryName: string): Boolean;
var
  Root: string;
begin
  Result := False;
  if DirectoryName = '' then
    Exit;
  Root := RemoveBackslashUnlessRoot(DirectoryName);
  Result :=
    FileExists(AddBackslash(Root) + 'client.exe') and
    FileExists(AddBackslash(Root) + 'portal.dat') and
    FileExists(AddBackslash(Root) + 'cell.dat');
end;

function DescribeMissingGameFiles(const DirectoryName: string): string;
var
  Root, Missing: string;
begin
  if DirectoryName = '' then
  begin
    Result := 'no folder selected';
    Exit;
  end;

  Root := RemoveBackslashUnlessRoot(DirectoryName);
  Missing := '';
  if not FileExists(AddBackslash(Root) + 'client.exe') then
    Missing := Missing + ' client.exe';
  if not FileExists(AddBackslash(Root) + 'portal.dat') then
    Missing := Missing + ' portal.dat';
  if not FileExists(AddBackslash(Root) + 'cell.dat') then
    Missing := Missing + ' cell.dat';
  if Missing = '' then
    Result := 'folder is complete'
  else
    Result := 'missing' + Missing + ' in ' + Root;
end;

function IsCompleteGameDirectoryOrDefault(const DirectoryName: string; var FoundDir: string): Boolean;
var
  Root, DefaultDir: string;
begin
  Result := False;
  if DirectoryName = '' then
    Exit;

  Root := RemoveBackslashUnlessRoot(DirectoryName);
  if IsCompleteGameDirectory(Root) then
  begin
    FoundDir := Root;
    Result := True;
    Exit;
  end;

  DefaultDir := AddBackslash(Root) + 'default';
  if IsCompleteGameDirectory(DefaultDir) then
  begin
    FoundDir := DefaultDir;
    Result := True;
  end;
end;

function FindCompleteGameSubfolder(const BaseDir: string; var FoundDir: string): Boolean;
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
          if IsCompleteGameDirectoryOrDefault(Candidate, FoundDir) then
          begin
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

function SameDirectory(const LeftName, RightName: string): Boolean;
begin
  Result := Lowercase(RemoveBackslashUnlessRoot(LeftName)) =
    Lowercase(RemoveBackslashUnlessRoot(RightName));
end;

function DirectoryIsUnder(const DirectoryName, ParentName: string): Boolean;
var
  Root, Parent: string;
begin
  Result := False;
  if (DirectoryName = '') or (ParentName = '') then
    Exit;
  Root := RemoveBackslashUnlessRoot(DirectoryName);
  Parent := RemoveBackslashUnlessRoot(ParentName);
  Result := SameDirectory(Root, Parent) or
    (Pos(Lowercase(AddBackslash(Parent)), Lowercase(AddBackslash(Root))) = 1);
end;

function TryExpandConstant(const ConstantName: string; var Value: string): Boolean;
begin
  Result := False;
  try
    Value := ExpandConstant(ConstantName);
    Result := Value <> '';
  except
    Result := False;
  end;
end;

function IsAetheriumPlayDirectory(const DirectoryName: string): Boolean;
var
  Candidate: string;
begin
  Result := False;
  if DirectoryName = '' then
    Exit;

  { {app} is not initialized during InitializeWizard. Never expand it unguarded. }
  if TryExpandConstant('{app}', Candidate) and DirectoryIsUnder(DirectoryName, Candidate) then
  begin
    Result := True;
    Exit;
  end;

  if TryExpandConstant('{autopf}\Aetherium Play', Candidate) and
     DirectoryIsUnder(DirectoryName, Candidate) then
  begin
    Result := True;
    Exit;
  end;

  Result :=
    (TryExpandConstant('{pf32}\Aetherium Play', Candidate) and
     DirectoryIsUnder(DirectoryName, Candidate)) or
    (TryExpandConstant('{localappdata}\Programs\Aetherium Play', Candidate) and
     DirectoryIsUnder(DirectoryName, Candidate));
end;

function TryAcceptGameDirectory(const DirectoryName: string; var FoundDir: string): Boolean;
begin
  Result := False;
  if IsAetheriumPlayDirectory(DirectoryName) then
    Exit;
  Result :=
    IsCompleteGameDirectoryOrDefault(DirectoryName, FoundDir) or
    FindCompleteGameSubfolder(DirectoryName, FoundDir);
  if Result and IsAetheriumPlayDirectory(FoundDir) then
  begin
    FoundDir := '';
    Result := False;
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

function TryRegistryPath(
  RootKey: Integer; const Subkey, ValueName: string; var FoundDir: string): Boolean;
var
  Candidate: string;
begin
  Result := False;
  if not RegQueryStringValue(RootKey, Subkey, ValueName, Candidate) then
    Exit;
  Result := TryAcceptGameDirectory(Trim(Candidate), FoundDir);
end;

function TryGetInstallDirFromGameRegistry(var FoundDir: string): Boolean;
var
  KeyPath: string;
begin
  KeyPath := 'SOFTWARE\Microsoft\Microsoft Games\Asheron''s Call\1.00';
  Result :=
    TryRegistryPath(HKLM, KeyPath, 'InstallationDirectory', FoundDir) or
    TryRegistryPath(HKLM, KeyPath, 'path', FoundDir) or
    TryRegistryPath(HKLM, KeyPath, 'Path', FoundDir) or
    TryRegistryPath(HKLM, KeyPath, 'Portal Dat', FoundDir) or
    TryRegistryPath(HKCU, KeyPath, 'InstallationDirectory', FoundDir) or
    TryRegistryPath(HKCU, KeyPath, 'path', FoundDir);
  if Result then
    Exit;

  if IsWin64 then
  begin
    Result :=
      TryRegistryPath(HKLM64, KeyPath, 'InstallationDirectory', FoundDir) or
      TryRegistryPath(HKLM64, KeyPath, 'path', FoundDir) or
      TryRegistryPath(HKLM64, KeyPath, 'Portal Dat', FoundDir);
  end;
end;

function TryGetGameRegistryHint: string;
var
  KeyPath, Candidate: string;
begin
  Result := '';
  KeyPath := 'SOFTWARE\Microsoft\Microsoft Games\Asheron''s Call\1.00';
  if RegQueryStringValue(HKLM, KeyPath, 'InstallationDirectory', Candidate) or
     RegQueryStringValue(HKLM, KeyPath, 'path', Candidate) or
     RegQueryStringValue(HKCU, KeyPath, 'InstallationDirectory', Candidate) or
     RegQueryStringValue(HKCU, KeyPath, 'path', Candidate) then
  begin
    Candidate := RemoveBackslashUnlessRoot(Trim(Candidate));
    if DirExists(Candidate) then
      Result := Candidate;
  end;
end;

function TryGetInstallDirFromUninstallRegistry(var FoundDir: string): Boolean;
var
  Roots: array[0..1] of string;
  RootKeys: array[0..1] of Integer;
  RootKeyCount, RootIdx, Index, KeyIdx: Integer;
  SubKeys: TArrayOfString;
  KeyPath, DisplayName, InstallLocation, UninstallString, Candidate: string;
begin
  Result := False;
  Roots[0] := 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall';
  Roots[1] := 'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall';
  RootKeys[0] := HKLM;
  RootKeyCount := 1;
  if IsWin64 then
  begin
    RootKeys[1] := HKLM64;
    RootKeyCount := 2;
  end;

  for KeyIdx := 0 to RootKeyCount - 1 do
  begin
    for RootIdx := 0 to 1 do
    begin
      if not RegGetSubkeyNames(RootKeys[KeyIdx], Roots[RootIdx], SubKeys) then
        Continue;

      for Index := 0 to GetArrayLength(SubKeys) - 1 do
      begin
        KeyPath := Roots[RootIdx] + '\' + SubKeys[Index];
        if not RegQueryStringValue(RootKeys[KeyIdx], KeyPath, 'DisplayName', DisplayName) then
          Continue;
        if Pos('asheron', Lowercase(DisplayName)) = 0 then
          Continue;

        if RegQueryStringValue(RootKeys[KeyIdx], KeyPath, 'InstallLocation', InstallLocation) then
        begin
          Candidate := Trim(InstallLocation);
          if TryAcceptGameDirectory(Candidate, FoundDir) then
          begin
            Result := True;
            Exit;
          end;
        end;

        if RegQueryStringValue(RootKeys[KeyIdx], KeyPath, 'UninstallString', UninstallString) then
        begin
          Candidate := ExtractFileDir(CleanUninstallString(UninstallString));
          if TryAcceptGameDirectory(Candidate, FoundDir) then
          begin
            Result := True;
            Exit;
          end;
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
            if TryAcceptGameDirectory(Candidate, FoundDir) then
            begin
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

function TryKnownGameDirectories(var FoundDir: string): Boolean;
var
  Candidates: array[0..14] of string;
  I, Count: Integer;
  PreviousRedir: Boolean;
begin
  Result := False;
  Candidates[0] := ExpandConstant('{param:GAMEINSTALLDIR|}');
  Candidates[1] := 'C:\asheronscalldm';
  Candidates[2] := 'C:\Turbine\Asheron''s Call';
  Candidates[3] := 'C:\Turbine Entertainment Software\Asheron''s Call';
  Candidates[4] := ExpandConstant('{pf32}\Turbine\Asheron''s Call');
  Candidates[5] := ExpandConstant('{pf32}\Turbine Entertainment Software\Asheron''s Call');
  Candidates[6] := ExpandConstant('{pf32}\Microsoft Games\Asheron''s Call');
  Candidates[7] := ExpandConstant('{localappdata}\VirtualStore\Program Files (x86)\Turbine\Asheron''s Call');
  Candidates[8] := ExpandConstant('{localappdata}\VirtualStore\Turbine\Asheron''s Call');
  Count := 9;
  if IsWin64 then
  begin
    Candidates[9] := ExpandConstant('{pf64}\Turbine\Asheron''s Call');
    Candidates[10] := ExpandConstant('{pf64}\Turbine Entertainment Software\Asheron''s Call');
    Candidates[11] := ExpandConstant('{pf64}\Microsoft Games\Asheron''s Call');
    Count := 12;
  end;

  PreviousRedir := EnableFsRedirection(False);
  try
    for I := 0 to Count - 1 do
    begin
      if TryAcceptGameDirectory(Candidates[I], FoundDir) then
      begin
        Result := True;
        Exit;
      end;
    end;

    Result :=
      TryGetInstallDirFromGameRegistry(FoundDir) or
      TryAcceptGameDirectory('C:\Turbine', FoundDir) or
      TryAcceptGameDirectory('C:\Turbine Entertainment Software', FoundDir) or
      TryAcceptGameDirectory(ExpandConstant('{pf32}\Turbine'), FoundDir) or
      TryAcceptGameDirectory(ExpandConstant('{pf32}\Turbine Entertainment Software'), FoundDir) or
      TryAcceptGameDirectory(ExpandConstant('{pf32}\Microsoft Games'), FoundDir) or
      TryGetInstallDirFromUninstallRegistry(FoundDir) or
      SearchShortcutsRecursive(ExpandConstant('{commonprograms}'), FoundDir) or
      SearchShortcutsRecursive(ExpandConstant('{userprograms}'), FoundDir) or
      SearchShortcutsRecursive(ExpandConstant('{commondesktop}'), FoundDir) or
      SearchShortcutsRecursive(ExpandConstant('{userdesktop}'), FoundDir);
  finally
    EnableFsRedirection(PreviousRedir);
  end;
end;

function TryResolveGameInstallDir(var FoundDir: string): Boolean;
begin
  Result := TryKnownGameDirectories(FoundDir);
end;

procedure InitializeWizard();
begin
  LegacyGameInstallRequired := not TryResolveGameInstallDir(ResolvedGameInstallDir);
  if LegacyGameInstallRequired then
    LogSetup('No complete Dark Majesty folder found yet; the original installer will run.')
  else
    LogSetup('Using existing Dark Majesty folder: ' + ResolvedGameInstallDir);
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

function ProcessNameExists(const ProcessName: string): Boolean;
var
  TempFile: string;
  Content: AnsiString;
  ResultCode: Integer;
begin
  Result := False;
  TempFile := ExpandConstant('{tmp}\ap-proc-' + ProcessName + '.txt');
  if not Exec(
    ExpandConstant('{sys}\cmd.exe'),
    '/C tasklist /FI "IMAGENAME eq ' + ProcessName + '" /NH > "' + TempFile + '"',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) then
    Exit;
  Result := LoadStringFromFile(TempFile, Content) and
    (Pos(Lowercase(ProcessName), Lowercase(Content)) > 0);
end;

function LegacyInstallerEngineRunning: Boolean;
begin
  Result := ProcessNameExists('IDriver.exe') or ProcessNameExists('IsSetup.exe');
end;

procedure WaitForLegacyInstaller();
var
  ElapsedMs, QuietMs: Integer;
begin
  ElapsedMs := 0;
  QuietMs := 0;
  if TryResolveGameInstallDir(ResolvedGameInstallDir) then
  begin
    LogSetup('Resolved Dark Majesty folder: ' + ResolvedGameInstallDir);
    Exit;
  end;

  LogSetup('Waiting for the original Dark Majesty installer to finish copying files.');
  while ElapsedMs < 45 * 60 * 1000 do
  begin
    WizardForm.StatusLabel.Caption :=
      'Waiting for the Dark Majesty installer to finish copying files...';

    if TryResolveGameInstallDir(ResolvedGameInstallDir) then
    begin
      LogSetup('Resolved Dark Majesty folder: ' + ResolvedGameInstallDir);
      Exit;
    end;

    if LegacyInstallerEngineRunning then
      QuietMs := 0
    else if ElapsedMs >= 8000 then
    begin
      QuietMs := QuietMs + 1000;
      if QuietMs >= 4000 then
      begin
        if TryResolveGameInstallDir(ResolvedGameInstallDir) then
          LogSetup('Resolved Dark Majesty folder: ' + ResolvedGameInstallDir)
        else
          LogSetup('Original installer exited before a complete folder was found.');
        Exit;
      end;
    end;

    Sleep(1000);
    ElapsedMs := ElapsedMs + 1000;
  end;
end;

function BrowseStartDirectory: string;
var
  Hint: string;
begin
  Hint := TryGetGameRegistryHint;
  if (Hint <> '') and DirExists(Hint) and (not IsAetheriumPlayDirectory(Hint)) then
    Result := Hint
  else if DirExists('C:\Turbine\Asheron''s Call') then
    Result := 'C:\Turbine\Asheron''s Call'
  else if DirExists('C:\asheronscalldm') then
    Result := 'C:\asheronscalldm'
  else if DirExists('C:\Turbine') then
    Result := 'C:\Turbine'
  else
    Result := 'C:\';
end;

function LegacySetupExePath: string;
begin
  Result := ExpandConstant(
    '{commonappdata}\AetheriumPlay\Bootstrap\legacy\Disk1\setup.exe');
end;

procedure ExplainLegacyInstaller();
begin
  MsgBox(
    'The original Dark Majesty installer will open next.' + #13#10 + #13#10 +
    'Leave the destination as C:\Turbine\Asheron''s Call (the default) and ' +
    'finish that wizard completely.' + #13#10 + #13#10 +
    'Do not choose C:\Program Files (x86)\Aetherium Play. That folder is ' +
    'this launcher, not the game.',
    mbInformation,
    MB_OK);
end;

function TryLaunchLegacyInstaller: Boolean;
var
  ResultCode: Integer;
  SetupExe: string;
begin
  Result := False;
  SetupExe := LegacySetupExePath;
  if not FileExists(SetupExe) then
  begin
    LogSetup('Original installer is missing: ' + SetupExe);
    Exit;
  end;

  ExplainLegacyInstaller();
  if not Exec(
    SetupExe,
    '',
    ExtractFileDir(SetupExe),
    SW_SHOWNORMAL,
    ewWaitUntilTerminated,
    ResultCode) then
  begin
    LogSetup('Could not start the original Dark Majesty installer.');
    Exit;
  end;

  WaitForLegacyInstaller();
  Result := IsCompleteGameDirectory(ResolvedGameInstallDir) or
    TryResolveGameInstallDir(ResolvedGameInstallDir);
end;

procedure PromptForGameInstallDir();
var
  SelectedDirectory, AcceptedDir: string;
begin
  SelectedDirectory := BrowseStartDirectory;

  while True do
  begin
    if not BrowseForFolder(
      'Select the Dark Majesty game folder (client.exe, portal.dat, and cell.dat). ' +
      'Usually C:\Turbine\Asheron''s Call, not the Aetherium Play folder.',
      SelectedDirectory,
      False) then
    begin
      if FileExists(LegacySetupExePath) and
         (MsgBox(
            'A complete Dark Majesty folder is required.' + #13#10 + #13#10 +
            'Open the original Dark Majesty installer again?',
            mbConfirmation,
            MB_YESNO) = IDYES) and
         TryLaunchLegacyInstaller then
        Exit;
      Continue;
    end;

    if IsAetheriumPlayDirectory(SelectedDirectory) then
    begin
      LogSetup('Rejected Aetherium Play folder: ' + SelectedDirectory);
      MsgBox(
        'That is the Aetherium Play folder, not the Dark Majesty game.' + #13#10 + #13#10 +
        'After the original installer finishes, choose C:\Turbine\Asheron''s Call.',
        mbError,
        MB_OK);
      SelectedDirectory := BrowseStartDirectory;
      Continue;
    end;

    if TryAcceptGameDirectory(SelectedDirectory, AcceptedDir) then
    begin
      ResolvedGameInstallDir := AcceptedDir;
      LogSetup('User selected Dark Majesty folder: ' + AcceptedDir);
      Exit;
    end;

    LogSetup('Rejected folder: ' + DescribeMissingGameFiles(SelectedDirectory));
    MsgBox(
      'That folder is not a complete Dark Majesty install.' + #13#10 + #13#10 +
      DescribeMissingGameFiles(SelectedDirectory) + #13#10 + #13#10 +
      'The original installer usually creates C:\Turbine\Asheron''s Call.',
      mbError,
      MB_OK);
  end;
end;

procedure EnsureGameInstallDir();
var
  Attempts: Integer;
begin
  if IsCompleteGameDirectory(ResolvedGameInstallDir) then
    Exit;
  if TryResolveGameInstallDir(ResolvedGameInstallDir) then
    Exit;

  WaitForLegacyInstaller();
  if IsCompleteGameDirectory(ResolvedGameInstallDir) then
    Exit;
  if TryResolveGameInstallDir(ResolvedGameInstallDir) then
    Exit;

  LogSetup('No complete Dark Majesty folder after the first installer pass.');
  for Attempts := 1 to 2 do
  begin
    if TryLaunchLegacyInstaller then
      Exit;
  end;

  PromptForGameInstallDir();
end;

function GetGameInstallDir(Param: string): string;
begin
  if not IsCompleteGameDirectory(ResolvedGameInstallDir) then
    TryResolveGameInstallDir(ResolvedGameInstallDir);
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
