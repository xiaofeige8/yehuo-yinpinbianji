[Setup]
AppId={{8CC32A25-0352-4764-9A06-01966344B461}
AppName=野火音频编辑
AppVersion=2.0.2
AppVerName=野火音频编辑 2.0.2 小程序卡密版
DefaultDirName={localappdata}\Programs\WildfireAudioEditor
DefaultGroupName=野火音频编辑
PrivilegesRequired=lowest
MinVersion=10.0
OutputDir=artifacts
OutputBaseFilename=野火音频编辑-2.0.2-GG卡密版安装程序
SetupIconFile=app.ico
UninstallDisplayIcon={app}\野火音频编辑.exe
UninstallDisplayName=野火音频编辑
VersionInfoProductName=野火音频编辑
VersionInfoDescription=野火音频编辑小程序卡密版安装程序
VersionInfoVersion=2.0.2
Compression=lzma2/normal
SolidCompression=yes
WizardStyle=modern
DisableWelcomePage=yes
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "SimplifiedChinese.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："

[Files]
Source: "stage\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\野火音频编辑"; Filename: "{app}\野火音频编辑.exe"; WorkingDir: "{app}"
Name: "{group}\使用说明"; Filename: "{app}\使用说明.txt"
Name: "{autodesktop}\野火音频编辑"; Filename: "{app}\野火音频编辑.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\野火音频编辑.exe"; Description: "打开野火音频编辑"; Flags: nowait postinstall skipifsilent unchecked
