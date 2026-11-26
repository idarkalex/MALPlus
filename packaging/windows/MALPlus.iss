#define MyAppName "MAL+"
#ifndef MyAppVersion
  #define MyAppVersion "0.8.0"
#endif
#define MyAppPublisher "idarkalex"
#define MyAppExeName "MALPlus.exe"
#ifndef AppBuildDir
  #define AppBuildDir "singlefile_v077"
#endif
#ifndef OutputFilename
  #define OutputFilename "MALPlus-Setup-v" + MyAppVersion
#endif

[Setup]
AppId={{C6B8D32A-725F-4A9A-B82E-64219FA5877E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\MALPlus
DefaultGroupName={#MyAppName}
DisableDirPage=no
DisableProgramGroupPage=yes
DisableReadyPage=no
PrivilegesRequired=lowest
OutputDir=output
OutputBaseFilename={#OutputFilename}
SetupIconFile=appicon.ico
WizardImageFile=wizard_large.bmp
WizardSmallImageFile=wizard_small.bmp
Compression=lzma2/normal
SolidCompression=yes
WizardStyle=modern dark includetitlebar hidebevels
WizardBackColor=#000D1A
WizardBackColorDynamicDark=#000D1A
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[InstallDelete]
; Clean up any loose files from earlier unorganized versions in {app}
Type: filesandordirs; Name: "{app}\app"
Type: filesandordirs; Name: "{app}\Microsoft.UI.Xaml"
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.png"
Type: files; Name: "{app}\*.jpg"
Type: files; Name: "{app}\*.winmd"
Type: files; Name: "{app}\*.pri"
Type: files; Name: "{app}\*.json"
Type: files; Name: "{app}\*.ttf"
Type: files; Name: "{app}\*.mui"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\MALPlus.exe.WebView2"
Type: filesandordirs; Name: "{app}"

[Files]
Source: "{#AppBuildDir}\MALPlus.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autoprograms}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// Force-kill any background instance of MALPlus before extracting new files
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  Exec('cmd.exe', '/c taskkill /F /IM MALPlus.exe /T 2>nul', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CleanOldFiles();
var
  FindRec: TFindRec;
  AppDir: string;
begin
  AppDir := ExpandConstant('{app}');
  if not DirExists(AppDir) then Exit;

  if FindFirst(AppDir + '\*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          // Don't delete uninstall files if they exist
          if (Pos('unins', LowerCase(FindRec.Name)) <> 1) then
          begin
            if (FindRec.Attributes and 16) <> 0 then
              DelTree(AppDir + '\' + FindRec.Name, True, True, True)
            else
              DeleteFile(AppDir + '\' + FindRec.Name);
          end;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    CleanOldFiles();
  end;
end;

var
  CleanUserData: Boolean;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  CleanUserData := False;

  // Make sure any background instance of MALPlus is closed
  Exec('cmd.exe', '/c taskkill /F /IM MALPlus.exe /T 2>nul', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if MsgBox('Do you also want to delete your saved login credentials, offline cache, and settings?' + #13#10 + #13#10 +
            '• Click Yes to completely wipe all user data, saved accounts, and cache.' + #13#10 +
            '• Click No to keep your login sessions and settings for future reinstalls.',
            mbConfirmation, MB_YESNO or MB_DEFBUTTON1) = IDYES then
  begin
    CleanUserData := True;
  end;
end;

procedure DeleteUserDataFolders();
var
  FindRec: TFindRec;
  LocalApp: string;
begin
  LocalApp := ExpandConstant('{localappdata}');

  // 1. Delete any com.idarkalex.malplus subdirectories under {localappdata}
  if FindFirst(LocalApp + '\*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Name <> '.') and (FindRec.Name <> '..') and ((FindRec.Attributes and 16) <> 0) then
        begin
          if DirExists(LocalApp + '\' + FindRec.Name + '\com.idarkalex.malplus') then
          begin
            DelTree(LocalApp + '\' + FindRec.Name + '\com.idarkalex.malplus', True, True, True);
          end;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;

  // 2. Direct known paths
  DelTree(LocalApp + '\User Name\com.idarkalex.malplus', True, True, True);
  DelTree(LocalApp + '\com.idarkalex.malplus', True, True, True);
  DelTree(LocalApp + '\MALPlus', True, True, True);
  DelTree(LocalApp + '\Temp\.net\MALPlus', True, True, True);
  DelTree(ExpandConstant('{userappdata}\MALPlus'), True, True, True);
  DelTree(ExpandConstant('{app}\MALPlus.exe.WebView2'), True, True, True);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDir: string;
begin
  if CurUninstallStep = usUninstall then
  begin
    AppDir := ExpandConstant('{app}');
    if DirExists(AppDir) then
    begin
      DelTree(AppDir + '\MALPlus.exe.WebView2', True, True, True);
    end;
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    if CleanUserData then
    begin
      DeleteUserDataFolders();
    end;

    // Delete any remaining files/folders inside {app} and remove the directory itself
    AppDir := ExpandConstant('{app}');
    if DirExists(AppDir) then
    begin
      DelTree(AppDir + '\MALPlus.exe.WebView2', True, True, True);
      DelTree(AppDir, True, True, True);
      RemoveDir(AppDir);
    end;
  end;
end;
