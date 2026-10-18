#ifndef AppVersion
  #define AppVersion "0.8.0"
#endif
#define AppName "모루"
#define SourceDir "..\artifacts\Moru-win-x64"

[Setup]
AppId={{93497921-596B-44E4-A1EE-AC4CF457F8AE}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=heopath
AppPublisherURL=https://github.com/heopath/moru-desktop
AppSupportURL=https://github.com/heopath/moru-desktop/issues
AppUpdatesURL=https://github.com/heopath/moru-desktop/releases
DefaultDirName={localappdata}\Programs\Moru
DefaultGroupName=모루
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts
OutputBaseFilename=Moru-Setup-{#AppVersion}-win-x64
SetupIconFile=..\src\Moru\Assets\Moru.ico
UninstallDisplayIcon={app}\Moru.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AppMutex=Local\Moru.DesktopCompanion.Install
SetupMutex=Local\Moru.DesktopCompanion.Setup
UninstallDisplayName=모루 (Moru)
VersionInfoVersion={#AppVersion}
VersionInfoDescription=모루 Windows 설치 프로그램
LicenseFile=..\LICENSE

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "settings.json,UserSkins\*,private-skins\*,diagnostics.log"

[Icons]
Name: "{userdesktop}\모루 실행"; Filename: "{app}\Moru.exe"; Parameters: "--show-panel"; WorkingDir: "{app}"
Name: "{userprograms}\모루\모루 실행"; Filename: "{app}\Moru.exe"; Parameters: "--show-panel"; WorkingDir: "{app}"
Name: "{userprograms}\모루\모루 제거"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\Moru.exe"; Parameters: "--install-integrations"; Flags: runhidden waituntilterminated
Filename: "{app}\Moru.exe"; Parameters: "--show-panel"; Description: "모루 실행"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\Moru.exe"; Parameters: "--uninstall-integrations"; Flags: runhidden waituntilterminated; RunOnceId: "MoruIntegrationCleanup"

; No autostart task or Run entry is created. Existing opt-in is preserved by the app helper.
; User preferences and PNGs live outside {app}; uninstall deliberately keeps them.
