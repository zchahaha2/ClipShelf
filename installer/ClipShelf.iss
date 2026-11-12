; 截图盒 ClipShelf 安装包（Inno Setup 6）
; 编译：在仓库根目录运行 build-installer.cmd，输出在 dist\
; 按当前用户安装到 %LOCALAPPDATA%\Programs\ClipShelf，不需要管理员权限
; （截图盒把截图和设置存在 exe 旁边，所以必须装在用户自己能写的位置）

#define AppVer "1.3.0"

[Setup]
AppId={{B86E6B2C-8074-4FD3-8C6F-D3CC25076671}
AppName=截图盒
AppVersion={#AppVer}
AppVerName=截图盒 {#AppVer}
AppPublisher=zchahaha2
AppPublisherURL=https://github.com/zchahaha2/ClipShelf
AppSupportURL=https://github.com/zchahaha2/ClipShelf/issues
AppUpdatesURL=https://github.com/zchahaha2/ClipShelf/releases
DefaultDirName={autopf}\ClipShelf
DefaultGroupName=截图盒
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=ClipShelf-Setup-{#AppVer}
SetupIconFile=..\ClipShelf.ico
UninstallDisplayIcon={app}\ClipShelf.exe
UninstallDisplayName=截图盒 ClipShelf
VersionInfoVersion={#AppVer}.0
VersionInfoProductName=截图盒 ClipShelf
VersionInfoDescription=截图盒 安装程序
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
MinVersion=10.0
ShowLanguageDialog=no

[Languages]
Name: "chs"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "其他选项："
Name: "autostart"; Description: "开机自动启动截图盒"; GroupDescription: "其他选项："

[Files]
Source: "..\ClipShelf.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\截图盒"; Filename: "{app}\ClipShelf.exe"; Comment: "截图自动收进来，拖出去就能发"
Name: "{autodesktop}\截图盒"; Filename: "{app}\ClipShelf.exe"; Comment: "截图自动收进来，拖出去就能发"; Tasks: desktopicon

[Registry]
; 和程序菜单里“开机自动启动”用的是同一个值，两边开关一致
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ClipShelf"; ValueData: """{app}\ClipShelf.exe"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\ClipShelf.exe"; Description: "现在运行截图盒"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 截图缓存、设置和日志都在程序文件夹里，卸载时一起清掉
Type: filesandordirs; Name: "{app}\shots"
Type: files; Name: "{app}\settings.ini"
Type: files; Name: "{app}\log.txt"
Type: files; Name: "{app}\log.txt.old"
Type: dirifempty; Name: "{app}"

[Code]
// 让从安装目录运行的截图盒先正常退出（只影响这个目录里的那一份）
procedure AskRunningCopyToQuit();
var
  Exe: String;
  Rc: Integer;
begin
  Exe := ExpandConstant('{app}\ClipShelf.exe');
  if FileExists(Exe) then
  begin
    Exec(Exe, '--quit', '', SW_HIDE, ewWaitUntilTerminated, Rc);
    Sleep(1500);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  AskRunningCopyToQuit();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  AskRunningCopyToQuit();
  Result := True;
end;

// 用户后来在程序菜单里打开的开机启动，卸载时也清掉（只清指向本安装目录的那一项）
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  V: String;
begin
  if CurUninstallStep = usPostUninstall then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ClipShelf', V) then
      if Pos(Lowercase(ExpandConstant('{app}')), Lowercase(V)) > 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ClipShelf');
end;
