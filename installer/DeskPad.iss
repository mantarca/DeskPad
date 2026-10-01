; DeskPad - Windows kurulum betigi (Inno Setup 6)
; Derle: ISCC.exe DeskPad.iss
; FFmpeg ve ADB, kurulum sirasinda EN GUNCEL surumleri indirilip acilir.
; Sanal ekran surucusu (VDD) kucuk oldugu icin pakete gomuludur.

#define AppName "DeskPad"
#define AppVersion "0.0.1"
#define AppPublisher "DeskPad"
#define AppExe "DeskPad.Desktop.exe"
#define SourceDir "C:\laragon\www\deskpad\final\DeskPad-PC"

[Setup]
AppId={{8F2B7C4E-3A91-4D2B-9C6E-1F5A7D3B9E21}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=C:\laragon\www\deskpad\final
OutputBaseFilename=DeskPad-Setup
Compression=lzma2/normal
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
SetupIconFile=C:\laragon\www\deskpad\host-pc\src\DeskPad.Desktop\Assets\app.ico

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaustu kisayolu olustur"; GroupDescription: "Ek kisayollar:"

[Files]
; Uygulama + kucuk surucu dosyalari + betikler
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{#AppName} Kaldir"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{#AppName} simdi baslatilsin mi?"; Flags: nowait postinstall skipifsilent

[Code]
const
  FFMPEG_URL = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip';
  ADB_URL    = 'https://dl.google.com/android/repository/platform-tools-latest-windows.zip';

var
  DownloadPage: TDownloadWizardPage;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage('Gerekli araclar indiriliyor',
    'DeskPad icin gerekli araclar (FFmpeg, ADB) internetten indiriliyor.', nil);
  DownloadPage.ShowBaseNameInsteadOfUrl := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Err: String;
begin
  Result := True;
  if CurPageID = wpReady then
  begin
    DownloadPage.Clear;
    DownloadPage.Add(FFMPEG_URL, 'ffmpeg.zip', '');
    DownloadPage.Add(ADB_URL, 'platform-tools.zip', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
      except
        if DownloadPage.AbortedByUser then
          Log('Kullanici indirmeyi iptal etti.')
        else
        begin
          Err := Format('%s: %s', [DownloadPage.LastBaseNameOrUrl, GetExceptionMessage]);
          SuppressibleMsgBox(Err, mbCriticalError, MB_OK, IDOK);
        end;
        Result := False;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

procedure ExtractZip(const Zip, Dest: String);
var
  Code: Integer;
begin
  ForceDirectories(Dest);
  if not Exec(ExpandConstant('{sys}\tar.exe'), '-xf "' + Zip + '" -C "' + Dest + '"', '',
              SW_HIDE, ewWaitUntilTerminated, Code) then
    MsgBox('Arsiv cikarilamadi (tar calistirilamadi): ' + Zip, mbError, MB_OK)
  else if Code <> 0 then
    MsgBox('Arsiv cikarilamadi (kod ' + IntToStr(Code) + '): ' + Zip, mbError, MB_OK);
end;

function FindFile(const Dir, FileName: String): String;
var
  FindRec: TFindRec;
  Sub: String;
begin
  Result := '';
  if FindFirst(AddBackslash(Dir) + '*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
          begin
            Sub := FindFile(AddBackslash(Dir) + FindRec.Name, FileName);
            if Sub <> '' then
            begin
              Result := Sub;
              Exit;
            end;
          end;
        end
        else if CompareText(FindRec.Name, FileName) = 0 then
        begin
          Result := AddBackslash(Dir) + FindRec.Name;
          Exit;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

procedure CopyFilesFlat(const SrcDir, DestDir: String);
var
  FindRec: TFindRec;
begin
  ForceDirectories(DestDir);
  if FindFirst(AddBackslash(SrcDir) + '*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
            CopyFilesFlat(AddBackslash(SrcDir) + FindRec.Name, DestDir);
        end
        else
          CopyFile(AddBackslash(SrcDir) + FindRec.Name, AddBackslash(DestDir) + FindRec.Name, False);
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

procedure InstallTools;
var
  Tmp, App, Raw, Found: String;
begin
  Tmp := ExpandConstant('{tmp}');
  App := ExpandConstant('{app}');
  ForceDirectories(App + '\tools');

  // ---- FFmpeg ----
  WizardForm.StatusLabel.Caption := 'FFmpeg cikariliyor...';
  Raw := Tmp + '\ffmpeg_raw';
  ExtractZip(Tmp + '\ffmpeg.zip', Raw);
  Found := FindFile(Raw, 'ffmpeg.exe');
  if Found <> '' then
  begin
    ForceDirectories(App + '\tools\ffmpeg');
    CopyFile(Found, App + '\tools\ffmpeg\ffmpeg.exe', False);
  end;

  // ---- ADB / platform-tools ----
  WizardForm.StatusLabel.Caption := 'ADB cikariliyor...';
  Raw := Tmp + '\adb_raw';
  ExtractZip(Tmp + '\platform-tools.zip', Raw);
  CopyFilesFlat(Raw, App + '\tools\adb');

  WizardForm.StatusLabel.Caption := 'Araclar hazir.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    try
      InstallTools;
    except
      MsgBox('Gerekli araclar cikarilamadi: ' + GetExceptionMessage, mbError, MB_OK);
    end;
  end;
end;
