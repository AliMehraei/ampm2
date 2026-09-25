; ampm2 installer (Inno Setup 6). Build with:  pwsh .\build-installer.ps1
; A "Prerequisites" page checks .NET 10 Desktop Runtime, Node.js and pm2, and offers to install what is missing.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "ampm2"
#define AppExe "ampm2.exe"
#define DotnetUrl "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"

[Setup]
AppId={{7C3A9E52-5B1D-4F7E-9A61-A2F0D5C8B311}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Ali Mehraei
AppPublisherURL=https://www.argbyte.com
AppSupportURL=https://github.com/AliMehraei/ampm2/issues
AppUpdatesURL=https://github.com/AliMehraei/ampm2/releases
AppComments=Lightweight Windows manager for pm2 processes
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
UsedUserAreasWarning=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\setup
OutputBaseFilename=ampm2-setup-{#AppVersion}
SetupIconFile=..\src\Ampm2\Assets\ampm2.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
LicenseFile=..\LICENSE.md
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
#ifndef TestBuild
AppMutex=Local\ampm2-single-instance
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "autostart"; Description: "Start ampm2 hidden in the &tray when I sign in"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion createallsubdirs
Source: "..\LICENSE.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Manage pm2 processes"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ampm2"; \
  ValueData: """{app}\{#AppExe}"" --autostart"; Tasks: autostart; Flags: uninsdeletevalue

[InstallDelete]
; the Start menu shortcut made by install.ps1 for the portable build
Type: files; Name: "{userprograms}\ampm2.lnk"

[Run]
; an existing "start as administrator without a UAC prompt" task (e.g. from the portable build) now launches this copy
Filename: "{app}\{#AppExe}"; Parameters: "--repoint-task"; Flags: runhidden waituntilterminated; StatusMsg: "Updating the elevated-launch task..."
; runasoriginaluser: ampm2 starts normally and elevates itself only if the pm2 daemon runs as administrator
Filename: "{app}\{#AppExe}"; Description: "Launch ampm2"; Flags: nowait postinstall skipifsilent runasoriginaluser
; an in-app update (About ▸ Install) runs silently with /RELAUNCH: reopen ampm2 when done
Filename: "{app}\{#AppExe}"; Parameters: "--updated"; Flags: nowait runasoriginaluser; Check: RelaunchRequested

[UninstallRun]
; the optional "start as administrator without a UAC prompt" task created from ampm2's Settings
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""ampm2 (elevated)"""; Flags: runhidden; RunOnceId: "DeleteElevatedTask"

[Code]
var
  PrereqPage: TWizardPage;
  DownloadPage: TDownloadWizardPage;
  CbDotnet, CbNode, CbPm2: TNewCheckBox;
  HasDotnet, HasNode, HasPm2: Boolean;

// ---------------- in-app update ----------------

// ampm2's About ▸ Install runs this setup with /SILENT /RELAUNCH.
function RelaunchRequested: Boolean;
begin
  Result := Pos('/RELAUNCH', UpperCase(GetCmdTail)) > 0;
end;

// ---------------- detection ----------------

function DotnetDesktop10Installed: Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), FindRec) then
  try
    repeat
      if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then Result := True;
    until Result or not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

function NodeDir: String;
var
  P: String;
begin
  Result := '';
  if FileExists(ExpandConstant('{commonpf64}\nodejs\node.exe')) then
    Result := ExpandConstant('{commonpf64}\nodejs')
  else if RegQueryStringValue(HKLM64, 'SOFTWARE\Node.js', 'InstallPath', P) and FileExists(AddBackslash(P) + 'node.exe') then
    Result := RemoveBackslash(P);
end;

function Pm2Installed: Boolean;
var
  Code: Integer;
begin
  Result := FileExists(ExpandConstant('{userappdata}\npm\pm2.cmd')) or
            ((NodeDir <> '') and FileExists(AddBackslash(NodeDir) + 'pm2.cmd'));
  if not Result then
    Result := Exec(ExpandConstant('{cmd}'), '/c where pm2 >nul 2>nul', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;

function WingetPath: String;
begin
  Result := ExpandConstant('{localappdata}\Microsoft\WindowsApps\winget.exe');
  if not FileExists(Result) then Result := '';
end;

// ---------------- prerequisites page ----------------

function AddLine(Top: Integer; const Installed: Boolean; const Title, Missing: String; var Cb: TNewCheckBox): Integer;
var
  L: TNewStaticText;
begin
  if Installed then
  begin
    L := TNewStaticText.Create(PrereqPage);
    L.Parent := PrereqPage.Surface;
    L.Top := Top;
    L.Left := ScaleX(4);
    L.Caption := #$2714 + '  ' + Title + ' is installed';
    L.Font.Color := $2E8B16;
    Cb := nil;
    Result := Top + ScaleY(26);
  end
  else
  begin
    Cb := TNewCheckBox.Create(PrereqPage);
    Cb.Parent := PrereqPage.Surface;
    Cb.Top := Top;
    Cb.Width := PrereqPage.SurfaceWidth;
    Cb.Height := ScaleY(18);
    Cb.Caption := 'Install ' + Title;
    Cb.Checked := True;
    L := TNewStaticText.Create(PrereqPage);
    L.Parent := PrereqPage.Surface;
    L.Top := Top + ScaleY(19);
    L.Left := ScaleX(20);
    L.Width := PrereqPage.SurfaceWidth - ScaleX(20);
    L.WordWrap := True;
    L.Caption := Missing;
    L.Font.Color := $707070;
    Result := Top + ScaleY(48);
  end;
end;

procedure InitializeWizard;
var
  Top: Integer;
  Intro: TNewStaticText;
begin
  HasDotnet := DotnetDesktop10Installed;
  HasNode := NodeDir <> '';
  HasPm2 := Pm2Installed;
#ifdef ForceMissing
  HasDotnet := False; HasNode := False; HasPm2 := False;   // layout test only
#endif

  PrereqPage := CreateCustomPage(wpLicense, 'Prerequisites',
    'ampm2 needs these components. Missing ones can be installed for you now.');
  Intro := TNewStaticText.Create(PrereqPage);
  Intro.Parent := PrereqPage.Surface;
  Intro.Width := PrereqPage.SurfaceWidth;
  Intro.WordWrap := True;
  Intro.Caption := 'Nothing is installed without your consent: untick anything you want to install yourself.';
  Top := Intro.Top + ScaleY(34);

  Top := AddLine(Top, HasDotnet, '.NET 10 Desktop Runtime',
    'Required to run ampm2. Downloaded from Microsoft (about 60 MB).', CbDotnet);
  Top := AddLine(Top, HasNode, 'Node.js LTS',
    'Needed by pm2. Installed with winget (Windows Package Manager).', CbNode);
  Top := AddLine(Top, HasPm2, 'pm2',
    'The process manager ampm2 controls. Installed with npm install -g pm2.', CbPm2);

  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

function WantDotnet: Boolean; begin Result := (CbDotnet <> nil) and CbDotnet.Checked; end;
function WantNode: Boolean;   begin Result := (CbNode <> nil) and CbNode.Checked; end;
function WantPm2: Boolean;    begin Result := (CbPm2 <> nil) and CbPm2.Checked; end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = PrereqPage.ID then
  begin
    if (not HasDotnet) and (not WantDotnet) then
      Result := MsgBox('ampm2 cannot start without the .NET 10 Desktop Runtime.' + #13#10 +
        'Continue without installing it?', mbConfirmation, MB_YESNO) = IDYES;
    if Result and WantPm2 and (not HasNode) and (not WantNode) then
      Result := MsgBox('pm2 needs Node.js, which is not installed. Continue anyway?', mbConfirmation, MB_YESNO) = IDYES;
  end
  else if (CurPageID = wpReady) and WantDotnet and (not WizardSilent) then
  begin
    DownloadPage.Clear;
    DownloadPage.Add('{#DotnetUrl}', 'windowsdesktop-runtime-10-win-x64.exe', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
      except
        if DownloadPage.AbortedByUser then
          Log('.NET download aborted by user')
        else
          SuppressibleMsgBox('Could not download the .NET 10 Desktop Runtime: ' + GetExceptionMessage + #13#10 +
            'Setup continues; install it later from https://dotnet.microsoft.com/download/dotnet/10.0', mbError, MB_OK, IDOK);
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo,
  MemoGroupInfo, MemoTasksInfo: String): String;
var
  S: String;
begin
  S := '';
  if WantDotnet then S := S + Space + '.NET 10 Desktop Runtime' + NewLine;
  if WantNode then S := S + Space + 'Node.js LTS' + NewLine;
  if WantPm2 then S := S + Space + 'pm2' + NewLine;
  Result := MemoDirInfo + NewLine;
  if MemoTasksInfo <> '' then Result := Result + NewLine + MemoTasksInfo + NewLine;
  if S <> '' then Result := Result + NewLine + 'Prerequisites to install:' + NewLine + S;
end;

// ---------------- installing prerequisites ----------------

procedure Status(const S: String);
begin
  WizardForm.StatusLabel.Caption := S;
  WizardForm.FilenameLabel.Caption := '';
end;

procedure InstallPrerequisites;
var
  Code: Integer;
  F, Npm: String;
begin
  // silent (in-app update or scripted): the prerequisites page was never shown, so install nothing unasked
  if WizardSilent then Exit;
  WizardForm.ProgressGauge.Style := npbstMarquee;
  try
    if WantDotnet then
    begin
      F := ExpandConstant('{tmp}\windowsdesktop-runtime-10-win-x64.exe');
      if FileExists(F) then
      begin
        Status('Installing .NET 10 Desktop Runtime...');
        if not Exec(F, '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, Code) or ((Code <> 0) and (Code <> 3010) and (Code <> 1638)) then
          SuppressibleMsgBox('The .NET 10 Desktop Runtime installer failed (code ' + IntToStr(Code) + ').', mbError, MB_OK, IDOK);
      end;
    end;

    if WantNode then
    begin
      Status('Installing Node.js LTS (winget)...');
      if WingetPath = '' then
      begin
        if SuppressibleMsgBox('winget is not available, so Node.js cannot be installed automatically.' + #13#10 +
          'Open the Node.js download page?', mbConfirmation, MB_YESNO, IDNO) = IDYES then
          ShellExec('open', 'https://nodejs.org/en/download', '', '', SW_SHOWNORMAL, ewNoWait, Code);
      end
      else if not Exec(WingetPath, 'install --id OpenJS.NodeJS.LTS -e --silent --scope machine --accept-source-agreements --accept-package-agreements --disable-interactivity',
        '', SW_HIDE, ewWaitUntilTerminated, Code) or ((Code <> 0) and (NodeDir = '')) then
        SuppressibleMsgBox('Node.js could not be installed (winget code ' + IntToStr(Code) + '). Install it from https://nodejs.org', mbError, MB_OK, IDOK);
    end;

    if WantPm2 then
    begin
      if NodeDir = '' then
        SuppressibleMsgBox('pm2 was not installed because Node.js is missing.', mbError, MB_OK, IDOK)
      else
      begin
        Status('Installing pm2 (npm install -g pm2)...');
        Npm := AddBackslash(NodeDir) + 'npm.cmd';
        // as the signed-in user, so pm2 lands in their npm folder and is not owned by an elevated process
        if not ExecAsOriginalUser(ExpandConstant('{cmd}'), '/d /s /c ""' + Npm + '" install -g pm2"', NodeDir, SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
          SuppressibleMsgBox('npm install -g pm2 failed (code ' + IntToStr(Code) + '). Run it yourself in a terminal.', mbError, MB_OK, IDOK);
      end;
    end;
  finally
    WizardForm.ProgressGauge.Style := npbstNormal;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then InstallPrerequisites;
end;

// ---------------- uninstall ----------------

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Dir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    Dir := ExpandConstant('{userappdata}\ampm2');
    if DirExists(Dir) and (SuppressibleMsgBox('Also remove your ampm2 settings (' + Dir + ')?' + #13#10 +
      'pm2 and your processes are not affected.', mbConfirmation, MB_YESNO, IDNO) = IDYES) then
      DelTree(Dir, True, True, True);
  end;
end;
