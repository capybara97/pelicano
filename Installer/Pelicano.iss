; 이 스크립트는 Pelicano의 단일 EXE 게시 결과를 기업 배포용 설치 프로그램으로 묶는다.
; 관리자 설치와 사용자 설치를 모두 허용하도록 PrivilegesRequiredOverridesAllowed를 활성화했다.

#define AppName "Pelicano"
#define AppVersion "0.5.1"
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
SetupIconFile={#SourceRoot}\Winui\Resources\app-icon.ico
UninstallDisplayIcon={app}\{#ExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=no
UsePreviousAppDir=yes
ShowLanguageDialog=yes
UsePreviousLanguage=yes
CloseApplications=yes
VersionInfoCompany={#Publisher}
VersionInfoDescription={#AppName} Installer
VersionInfoCopyright={#CreatorLine}

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"; LicenseFile: "license-ko.txt"; InfoBeforeFile: "privacy-ko.txt"
Name: "english"; MessagesFile: "compiler:Default.isl"; LicenseFile: "license-en.txt"; InfoBeforeFile: "privacy-en.txt"

[CustomMessages]
korean.AutoStartTask=Windows 시작 시 자동 실행
english.AutoStartTask=Run automatically when Windows starts

korean.DesktopIconTask=바탕 화면 바로 가기 생성
english.DesktopIconTask=Create a desktop shortcut

korean.RunApp={#AppName} 실행
english.RunApp=Launch {#AppName}

korean.CreatorLine=만든이 김민상
english.CreatorLine=Created by Minsang Kim

[Tasks]
Name: "autostart"; Description: "{cm:AutoStartTask}"; Flags: unchecked
Name: "desktopicon"; Description: "{cm:DesktopIconTask}"; Flags: unchecked

[Files]
Source: "{#PublishRoot}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
Source: "{#SourceRoot}\docs\보안팀_설명서.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#SourceRoot}\docs\개발자_가이드.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "license-ko.txt"; DestDir: "{app}\docs\legal"; DestName: "license-ko.txt"; Flags: ignoreversion
Source: "privacy-ko.txt"; DestDir: "{app}\docs\legal"; DestName: "privacy-ko.txt"; Flags: ignoreversion
Source: "license-en.txt"; DestDir: "{app}\docs\legal"; DestName: "license-en.txt"; Flags: ignoreversion
Source: "privacy-en.txt"; DestDir: "{app}\docs\legal"; DestName: "privacy-en.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#ExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#ExeName}"""; \
    Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#ExeName}"; Description: "{cm:RunApp}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\Pelicano"
Type: filesandordirs; Name: "{localappdata}\Pelicano\updates"

[Code]
procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel2.Caption :=
    WizardForm.WelcomeLabel2.Caption + #13#10#13#10 + ExpandConstant('{cm:CreatorLine}');
  WizardForm.FinishedLabel.Caption :=
    WizardForm.FinishedLabel.Caption + #13#10#13#10 + ExpandConstant('{cm:CreatorLine}');
end;
