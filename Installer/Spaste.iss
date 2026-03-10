; 이 스크립트는 Pelicano의 단일 EXE 게시 결과를 기업 배포용 설치 프로그램으로 묶는다.
; 관리자 설치와 사용자 설치를 모두 허용하도록 PrivilegesRequiredOverridesAllowed를 활성화했다.

#define AppName "Pelicano"
#define AppVersion "1.0.0"
#define Publisher "김민상"
#define CreatorLine "만든이 김민상"
#define ExeName "Pelicano.exe"
#define SourceRoot ".."
#define PublishRoot "..\publish"
#define OutputRoot "..\dist"

[Setup]
AppId={{B68344D9-62CB-451A-9B12-68D1FDD3449A}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#Publisher}
AppComments={#CreatorLine}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputRoot}
OutputBaseFilename=Pelicano-Installer
SetupIconFile={#SourceRoot}\Spaste\Resources\app.ico
UninstallDisplayIcon={app}\{#ExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=no
UsePreviousAppDir=yes
CloseApplications=no
VersionInfoCompany={#Publisher}
VersionInfoDescription={#AppName} Installer
VersionInfoCopyright={#CreatorLine}

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "autostart"; Description: "Windows 시작 시 자동 실행"; Flags: unchecked
Name: "desktopicon"; Description: "바탕 화면 바로 가기 생성"; Flags: unchecked

[Files]
Source: "{#PublishRoot}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
Source: "{#SourceRoot}\docs\보안팀_설명서.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#SourceRoot}\docs\개발자_가이드.md"; DestDir: "{app}\docs"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#ExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#ExeName}"""; \
    Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#ExeName}"; Description: "{#AppName} 실행"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\Pelicano"
Type: filesandordirs; Name: "{userappdata}\Spaste"

[Code]
procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel2.Caption :=
    WizardForm.WelcomeLabel2.Caption + #13#10#13#10 + '{#CreatorLine}';
  WizardForm.FinishedLabel.Caption :=
    WizardForm.FinishedLabel.Caption + #13#10#13#10 + '{#CreatorLine}';
end;
