; Aetherium Launcher installer
; Developed by Vanquish, aka Chosen One
;
; Drops the self-contained launcher (exe + Assets + x86 .NET runtime) into the
; player's existing Asheron's Call install folder (the one containing
; client.exe), and creates Start Menu / optional Desktop shortcuts.
;
; Build with:
;   "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" AetheriumLauncher.iss
;
; Requires the launcher to already be published self-contained first:
;   dotnet publish ..\AetheriumLauncher\AetheriumLauncher.csproj -c Release -r win-x86 --self-contained true

#define MyAppName "Aetherium Launcher"
#ifndef MyAppVersion
#define MyAppVersion "1.0.26"
#endif
#define MyAppPublisher "Vanquish (aka Chosen One)"
#define MyAppExeName "AetheriumLauncher.exe"
#define PublishDir "..\AetheriumLauncher\bin\Release\net8.0-windows\win-x86\publish"

[Setup]
AppId={{B7E6B8B4-9B9E-4E1B-9C7C-3A4F0F1D6C21}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
; Deliberately just the publisher folder, not "...\Asheron's Call" - the exact
; subfolder name/apostrophe character varies between installs (see the
; FindClientExeSubfolder comment below), so we let that logic detect the real
; subfolder by content instead of guessing its exact spelling here.
DefaultDirName=C:\Turbine Entertainment Software
; Without this, Inno remembers whatever folder was used/browsed-to on any
; earlier run of an installer with this same AppId (even a stale test run)
; and silently re-suggests it forever, overriding our own auto-detection below.
UsePreviousAppDir=no
DisableDirPage=no
DisableProgramGroupPage=yes
DisableWelcomePage=no
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=ASHERON.ICO
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=..\artifacts\installer
OutputBaseFilename=AetheriumLauncherSetup
ChangesAssociations=no
InfoBeforeFile=CommunityClientSource.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "skin\default"; Description: "&Default"; GroupDescription: "Launcher skin:"; Flags: exclusive
Name: "skin\pk"; Description: "&PK"; GroupDescription: "Launcher skin:"; Flags: exclusive unchecked

[Files]
; client.exe is deliberately not bundled. The verified community download in
; [Run] replaces it only after an exact SHA-256 match.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "CommunityClientSource.txt"; DestDir: "{app}"; DestName: "COMMUNITY_CLIENT_SOURCE.txt"; Flags: ignoreversion
Source: "..\ThirdParty\MegaApiClient\LICENSE"; DestDir: "{app}"; DestName: "MegaApiClient-LICENSE.txt"; Flags: ignoreversion
Source: "..\tools\dgvoodoo\NOTICE.md"; DestDir: "{app}"; DestName: "dgVoodoo-NOTICE.md"; Flags: ignoreversion
; dgVoodoo DirectDraw wrapper payload - GraphicsBootstrap.EnsureDirectDrawWrapper
; (run automatically on every Play click) copies DDraw.dll/D3DImm.dll from here
; into the game folder itself. This is what avoids the legacy "set your desktop
; to 16-bit" DirectDraw windowed-mode error on modern Windows. Without these
; files present next to the launcher, that step silently does nothing.
Source: "..\tools\dgvoodoo\extracted\MS\x86\DDraw.dll"; DestDir: "{app}\dgvoodoo\extracted\MS\x86"; Flags: ignoreversion
Source: "..\tools\dgvoodoo\extracted\MS\x86\D3DImm.dll"; DestDir: "{app}\dgvoodoo\extracted\MS\x86"; Flags: ignoreversion
Source: "..\tools\dgvoodoo\extracted\dgVoodoo.conf"; DestDir: "{app}\dgvoodoo\extracted"; Flags: ignoreversion

[InstallDelete]
; Version 1.0.5 and earlier used the internal project name for deployed files.
; Remove those stale binaries during an in-place upgrade, while retaining
; launcher.json and the legacy LocalAppData profile directory.
Type: files; Name: "{app}\AcLegacyLauncher.exe"
Type: files; Name: "{app}\AcLegacyLauncher.dll"
Type: files; Name: "{app}\AcLegacyLauncher.pdb"
Type: files; Name: "{app}\AcLegacyLauncher.deps.json"
Type: files; Name: "{app}\AcLegacyLauncher.runtimeconfig.json"

; client.exe reads UseHardware/DoubleBuffer/FullScreen/ZBuffer2 directly from
; HKEY_LOCAL_MACHINE (confirmed from the decompiled client source). The
; launcher also seeds these at runtime, but only via a *non-elevated* write,
; which Windows only transparently redirects back to this real HKLM location
; through UAC registry virtualization - and that's not guaranteed to apply
; for every user/config (e.g. UAC disabled, running as full Administrator).
; Since this installer already runs elevated, write the real values directly
; here so the correct settings exist regardless of virtualization behavior.
[Registry]
Root: HKLM; Subkey: "SOFTWARE\WOW6432Node\Microsoft\Microsoft Games\Asheron's Call\1.00"; ValueType: dword; ValueName: "UseHardware"; ValueData: "1"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\WOW6432Node\Microsoft\Microsoft Games\Asheron's Call\1.00"; ValueType: dword; ValueName: "DoubleBuffer"; ValueData: "2"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\WOW6432Node\Microsoft\Microsoft Games\Asheron's Call\1.00"; ValueType: dword; ValueName: "FullScreen"; ValueData: "1"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\WOW6432Node\Microsoft\Microsoft Games\Asheron's Call\1.00"; ValueType: dword; ValueName: "ZBuffer2"; ValueData: "0"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\WOW6432Node\Microsoft\Microsoft Games\Asheron's Call\1.00"; ValueType: dword; ValueName: "ScreenWidth"; ValueData: "800"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "SOFTWARE\WOW6432Node\Microsoft\Microsoft Games\Asheron's Call\1.00"; ValueType: dword; ValueName: "ScreenHeight"; ValueData: "600"; Flags: uninsdeletevalue

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--install-community-client-from-file ""{param:COMMUNITYCLIENTFILE|}"" ""{app}"""; StatusMsg: "Installing and verifying the community DM client..."; Check: HasCommunityClientFile; AfterInstall: VerifyCommunityClient; Flags: waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Parameters: "--install-community-client ""{app}"""; StatusMsg: "Downloading and verifying the community DM client (the MEGA ZIP may be about 200 MB; please wait)..."; Check: not HasCommunityClientFile; AfterInstall: VerifyCommunityClient; Flags: waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
function HasCommunityClientFile: Boolean;
begin
  Result := ExpandConstant('{param:COMMUNITYCLIENTFILE|}') <> '';
end;

procedure VerifyCommunityClient;
var
  ResultCode: Integer;
begin
  if (not Exec(
    ExpandConstant('{app}\{#MyAppExeName}'),
    '--verify-community-client "' + ExpandConstant('{app}') + '"',
    ExpandConstant('{app}'),
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode)) or (ResultCode <> 0) then
  begin
    RaiseException(
      'The community DM client was not installed because client.exe did not ' +
      'match the required SHA-256.' + #13#10 + #13#10 +
      'Required SHA-256:' + #13#10 +
      '52DDFDD1BD3AF839A90898C9A2A3BA8983E1811A1F1E45A588B649C5615DD26B' +
      '' + #13#10 + #13#10 + 'Community source:' + #13#10 +
      'https://mega.nz/folder/L1MniCKJ#1dQCCFPc2ddcFILa_JGeZw/folder/T00V3ISI');
  end;
end;

// If the selected folder itself doesn't have client.exe, check one level
// down - handles picking the parent folder (e.g. "Turbine Entertainment
// Software" instead of "...\Asheron's Call"). We deliberately find the real
// subfolder by scanning for client.exe rather than hardcoding its name:
// different AC releases/locales can spell "Asheron's Call" with a different
// apostrophe character (straight ' vs curly '), which would silently break a
// hardcoded string comparison even though the folder is really there.
// Returns True and sets FoundDir to the subfolder if a match is found.
function FindClientExeSubfolder(BaseDir: string; var FoundDir: string): Boolean;
var
  FindRec: TFindRec;
  Candidate: string;
begin
  Result := False;
  if FindFirst(AddBackslash(BaseDir) + '*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0) and
           (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          Candidate := AddBackslash(BaseDir) + FindRec.Name;
          if FileExists(AddBackslash(Candidate) + 'client.exe') then
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

// Strips surrounding quotes and (for an unquoted string) any trailing
// " /switches" from a registry UninstallString, leaving just the exe path.
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

// Every real Windows installer (InstallShield, MSI, Wise, etc.) registers an
// uninstall entry - this is the same mechanism "Add or Remove Programs" and
// most game-launcher "detect my install" features use. We scan both the
// 64-bit and 32-bit (WOW6432Node) uninstall lists for anything mentioning
// "Asheron", then try its InstallLocation value, falling back to the folder
// the uninstaller itself lives in. This finds the real folder no matter where
// the game was actually installed, custom location or not.
function TryGetInstallDirFromUninstallRegistry(var FoundDir: string): Boolean;
var
  Roots: array[0..1] of string;
  RootIdx, j: Integer;
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

    for j := 0 to GetArrayLength(SubKeys) - 1 do
    begin
      KeyPath := Roots[RootIdx] + '\' + SubKeys[j];
      if not RegQueryStringValue(HKLM, KeyPath, 'DisplayName', DisplayName) then
        Continue;
      if Pos('asheron', Lowercase(DisplayName)) = 0 then
        Continue;

      if RegQueryStringValue(HKLM, KeyPath, 'InstallLocation', InstallLocation) then
      begin
        Candidate := Trim(InstallLocation);
        if (Candidate <> '') and FileExists(AddBackslash(Candidate) + 'client.exe') then
        begin
          FoundDir := Candidate;
          Result := True;
          Exit;
        end;
      end;

      if RegQueryStringValue(HKLM, KeyPath, 'UninstallString', UninstallString) then
      begin
        Candidate := ExtractFileDir(CleanUninstallString(UninstallString));
        if (Candidate <> '') and FileExists(AddBackslash(Candidate) + 'client.exe') then
        begin
          FoundDir := Candidate;
          Result := True;
          Exit;
        end;
      end;
    end;
  end;
end;

// Recursively searches a Start Menu / Desktop folder for a shortcut mentioning
// "Asheron" and resolves its target - a second, independent way to find the
// real game folder when there's no usable uninstall registry entry.
function SearchShortcutsRecursive(const Folder: string; var FoundDir: string): Boolean;
var
  FindRec: TFindRec;
  Shell: Variant;
  ShortcutPath, TargetPath: string;
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
            if (Lowercase(ExtractFileName(TargetPath)) = 'client.exe') and FileExists(TargetPath) then
            begin
              FoundDir := ExtractFileDir(TargetPath);
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

function TryGetInstallDirFromShortcuts(var FoundDir: string): Boolean;
begin
  Result :=
    SearchShortcutsRecursive(ExpandConstant('{commonprograms}'), FoundDir) or
    SearchShortcutsRecursive(ExpandConstant('{userprograms}'), FoundDir) or
    SearchShortcutsRecursive(ExpandConstant('{commondesktop}'), FoundDir) or
    SearchShortcutsRecursive(ExpandConstant('{userdesktop}'), FoundDir);
end;

// Tries every detection method, cheapest/most reliable first.
function FindRealInstallDir(var FoundDir: string): Boolean;
begin
  Result :=
    TryGetInstallDirFromUninstallRegistry(FoundDir) or
    TryGetInstallDirFromShortcuts(FoundDir);
end;

// Pre-fill the directory page with the real, auto-detected install folder
// before the user even sees it, so most people can just click Next.
procedure InitializeWizard();
var
  DetectedDir: string;
begin
  if FindRealInstallDir(DetectedDir) then
    WizardForm.DirEdit.Text := DetectedDir;
end;

// Seed only the selected skin name. The launcher consumes this on first run and
// merges it into launcher.json, preserving any existing account/server settings.
procedure CurStepChanged(CurStep: TSetupStep);
var
  SkinName: string;
begin
  if CurStep = ssPostInstall then
  begin
    SkinName := 'default';
    if WizardIsTaskSelected('skin\pk') then
      SkinName := 'pk';
    SaveStringToFile(ExpandConstant('{app}\launcher.skin'), SkinName, False);
  end;
end;

// The launcher must be installed on top of an existing Asheron's Call
// install (it needs client.exe next to it to launch). Block Next until the
// chosen folder actually looks like a real AC install.
function NextButtonClick(CurPageID: Integer): Boolean;
var
  ChosenDir, ClientPath, SubDir: string;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    ChosenDir := WizardDirValue;
    ClientPath := AddBackslash(ChosenDir) + 'client.exe';
    if FileExists(ClientPath) then
      Exit;

    // Picked the parent folder by mistake? Auto-correct down into it.
    if FindClientExeSubfolder(ChosenDir, SubDir) then
    begin
      WizardForm.DirEdit.Text := SubDir;
      Exit;
    end;

    // Last resort before giving up: try the registry/shortcut detection
    // again in case the user cleared or mistyped the field.
    if FindRealInstallDir(SubDir) then
    begin
      WizardForm.DirEdit.Text := SubDir;
      Exit;
    end;

    MsgBox(
      'This folder does not contain client.exe, and no subfolder of it does ' +
      'either.' + #13#10 + #13#10 + 'Checked: ' + ClientPath + #13#10 + #13#10 +
      'Aetherium Launcher needs to be installed into your existing Asheron''s Call ' +
      'game folder (the one with client.exe in it - usually C:\Turbine ' +
      'Entertainment Software\Asheron''s Call).' + #13#10 + #13#10 + 'Please click ' +
      'Browse and pick that folder (or its parent folder) and try again.', mbError, MB_OK);
    Result := False;
  end;
end;
