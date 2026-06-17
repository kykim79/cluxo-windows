; Cluxo for Windows — Inno Setup 설치 스크립트.
; 빌드: ISCC.exe /DAppVersion=1.0.0 installer\cluxo.iss  → installer-out\Cluxo-Setup-<버전>.exe
; 사전: scripts/publish.ps1로 publish\win-x64\Cluxo.Windows.App.exe 생성돼 있어야 함.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{C1A0E0D0-1B2C-4D3E-9F40-436C75786F31}
AppName=Cluxo
AppVersion={#AppVersion}
AppPublisher=kykim79
AppPublisherURL=https://github.com/kykim79/cluxo-windows
DefaultDirName={autopf}\Cluxo
DefaultGroupName=Cluxo
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\Cluxo.Windows.App.exe
OutputDir=..\installer-out
OutputBaseFilename=Cluxo-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
; Cluxo는 관리자 권한 앱(requireAdministrator) — 설치도 Program Files라 관리자 필요.
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern
SetupIconFile=..\src\Cluxo.Windows.App\Assets\cluxo.ico

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "..\publish\win-x64\Cluxo.Windows.App.exe"; DestDir: "{app}"; Flags: ignoreversion
; 트레이 아이콘 파일(코브랜딩 교체 가능). exe 임베드 아이콘도 있어 없어도 동작하지만 명시 포함.
Source: "..\src\Cluxo.Windows.App\Assets\cluxo.ico"; DestDir: "{app}\Assets"; Flags: ignoreversion

[Icons]
Name: "{group}\Cluxo"; Filename: "{app}\Cluxo.Windows.App.exe"
Name: "{group}\{cm:UninstallProgram,Cluxo}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Cluxo"; Filename: "{app}\Cluxo.Windows.App.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Cluxo.Windows.App.exe"; Description: "{cm:LaunchProgram,Cluxo}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; 제거 시 실행 중인 Cluxo 종료
Filename: "taskkill"; Parameters: "/F /IM Cluxo.Windows.App.exe"; Flags: runhidden; RunOnceId: "KillCluxo"
