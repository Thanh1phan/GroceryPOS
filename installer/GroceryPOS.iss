; GroceryPOS installer (Inno Setup 6.3+). Normally built by build/publish.ps1, which publishes the app first and passes:
;   /DAppVersion=0.9.0  /DSourceDir=..\artifacts\publish\win-x64  [/DFrameworkDependent=1]
; Manual build: iscc installer\GroceryPOS.iss  (uses the defaults below).
;
; The program folder only holds GroceryPOS.exe + appsettings.json. The shop's data (database, logs, backups,
; appsettings.local.json) lives in %LocalAppData%\GroceryPOS of each Windows user (see AppDataLocation), so upgrading
; or uninstalling never touches it. Thông tin phần mềm in the app opens that folder.

#ifndef AppVersion
  #define AppVersion "0.9.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\win-x64"
#endif
#ifndef FrameworkDependent
  #define FrameworkDependent "0"
#endif
#if FrameworkDependent == "1"
  #define OutputSuffix "-framework"
#else
  #define OutputSuffix ""
#endif

#define AppName "GroceryPOS"
#define AppExe "GroceryPOS.exe"
; Must match SingleInstance.InstallerMutexName in src/GroceryPOS.App/Hosting/SingleInstance.cs.
#define AppMutexName "GroceryPOS.App.Running"

[Setup]
AppId={{6B1E2F4C-3D7A-4B8E-9C51-0A2F7E4D9B63}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=GroceryPOS
AppComments=Phần mềm quản lý bán hàng cho cửa hàng tạp hóa
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Admin install to Program Files by default; a cashier account without admin rights can choose "chỉ cho tôi".
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
AppMutex={#AppMutexName}
CloseApplications=yes
RestartApplications=no
SetupIconFile=..\src\GroceryPOS.App\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName} {#AppVersion}
OutputDir=..\artifacts\installer
OutputBaseFilename=GroceryPOS-Setup-{#AppVersion}{#OutputSuffix}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
; Vietnamese.isl is an unofficial translation (jrsoftware.org/files/istrans/); used when present in Inno's Languages folder.
#if FileExists(AddBackslash(CompilerPath) + "Languages\Vietnamese.isl")
Name: "vietnamese"; MessagesFile: "compiler:Languages\Vietnamese.isl"
#endif
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
DesktopIcon=Tạo biểu tượng trên màn hình
AdditionalIcons=Biểu tượng:
LaunchApp=Mở GroceryPOS
RuntimeMissing=Máy này chưa có .NET Desktop Runtime 8.0 (x64) mà bản cài đặt gọn cần để chạy.%n%nBấm OK để mở trang tải về (chọn ".NET Desktop Runtime 8.0 – Windows x64"), cài xong rồi chạy lại bộ cài này.%nHoặc dùng bộ cài đầy đủ GroceryPOS-Setup-{#AppVersion}.exe (không cần cài thêm gì).
DataKept=Đã gỡ GroceryPOS. Dữ liệu cửa hàng (cơ sở dữ liệu, bản sao lưu, nhật ký) vẫn được giữ trong:%n%1%n%nXóa thư mục này nếu bạn không dùng GroceryPOS nữa.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "portable.txt,*.db,*.db-wal,*.db-shm,logs\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[Code]
#if FrameworkDependent == "1"
function HasDesktopRuntime8(): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec) then
  begin
    try
      repeat
        if FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0 then
          Result := True;
      until Result or not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := HasDesktopRuntime8();
  if not Result then
  begin
    if SuppressibleMsgBox(CustomMessage('RuntimeMissing'), mbError, MB_OKCANCEL, IDCANCEL) = IDOK then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/8.0', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;
end;
#endif

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\{#AppName}');
    if DirExists(DataDir) and not UninstallSilent() then
      MsgBox(FmtMessage(CustomMessage('DataKept'), [DataDir]), mbInformation, MB_OK);
  end;
end;
