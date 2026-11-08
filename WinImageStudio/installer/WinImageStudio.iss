#define AppVersion "0.6.1"
[Setup]
AppId=WinImageStudio.Desktop
AppName=WinImageStudio
AppVersion={#AppVersion}
AppPublisher=WinImageStudio
DefaultDirName={autopf}\WinImageStudio
DefaultGroupName=WinImageStudio
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19045
OutputDir=..\artifacts\setup
OutputBaseFilename=WinImageStudio-Setup-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=110
LicenseFile=..\LICENSE
InfoBeforeFile=INSTALL.ar.txt
UninstallDisplayIcon={app}\WinImageStudio.exe
AppMutex=WinImageStudio.Desktop
CloseApplications=no
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "إنشاء اختصار على سطح المكتب"; Flags: checkedonce
Name: "installadk"; Description: "تجهيز أدوات ISO من مثبت Microsoft ADK المرفق (Deployment Tools فقط؛ قد يلزم الإنترنت)"; Flags: checkedonce

[Files]
Source: "..\artifacts\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\WinImageStudio"; Filename: "{app}\WinImageStudio.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\WinImageStudio"; Filename: "{app}\WinImageStudio.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\WinImageStudio.exe"; Description: "فتح WinImageStudio"; Flags: nowait postinstall skipifsilent runascurrentuser

[Code]
var
  AdkNeedsRestart: Boolean;
  AdkFailed: Boolean;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExitCode: Integer;
  Started: Boolean;
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('installadk') then
  begin
    WizardForm.StatusLabel.Caption := 'تجهيز أدوات ISO والتحقق من ملف ADK المرفق...';
    Started := Exec(ExpandConstant('{app}\WinImageStudio.exe'), '--install-bundled-adk', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ExitCode);
    if Started and ((ExitCode = 0) or (ExitCode = 3010)) then
    begin
      AdkNeedsRestart := ExitCode = 3010;
      Log('Bundled ADK setup completed. Exit code: ' + IntToStr(ExitCode));
    end
    else
    begin
      AdkFailed := True;
      Log('WinImageStudio installed, but ADK setup did not complete. Exit code: ' + IntToStr(ExitCode));
      if not WizardSilent then
        MsgBox('تم تثبيت WinImageStudio، لكن تجهيز ADK لم يكتمل. تحقق من الإنترنت ثم أعد المحاولة من زر تجهيز أدوات ISO داخل البرنامج. سجل التفاصيل موجود في LOCALAPPDATA\WinImageStudio\crash.log. لم تُحذف مشاريعك أو ملفات ISO.', mbError, MB_OK);
    end;
  end;
end;

function NeedRestart(): Boolean;
begin
  Result := AdkNeedsRestart;
end;

function GetCustomSetupExitCode(): Integer;
begin
  if AdkFailed then Result := 101 else Result := 0;
end;
