; DeskPad - Windows kurulum betigi (Inno Setup 6)
; Derle: ISCC.exe DeskPad.iss

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
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{#AppName} Kaldir"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{#AppName} simdi baslatilsin mi?"; Flags: nowait postinstall skipifsilent
