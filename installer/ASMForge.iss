; ASMForge installer (Inno Setup 6, https://jrsoftware.org/isinfo.php).
; Built by publish.ps1, which passes AppVersion, SourceDir (the published app folder) and OutputDir.
;
; Installs per user (no administrator password): the app goes in %LOCALAPPDATA%\Programs\ASMForge, with a
; Start Menu shortcut, an optional desktop shortcut, optional .asm/.pseudo file associations, and an
; uninstaller in Windows Settings > Apps. Settings and logs in %LOCALAPPDATA%\ASMForge are kept on uninstall.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\ASMForge-" + AppVersion + "-win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\publish"
#endif

#define AppName "ASMForge"
#define AppExe "ASMForge.App.exe"

[Setup]
; AppId identifies ASMForge across versions so new installers upgrade the existing install. Never change it.
AppId={{726BBCAE-EDD7-4178-A6F3-599C3C24D425}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=ASMForge
AppComments=MIPS assembly IDE and simulator
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#OutputDir}
OutputBaseFilename=ASMForge-Setup-{#AppVersion}
SetupIconFile=..\src\ASMForge.App\Assets\asmforge.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName} {#AppVersion}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Tell Explorer to refresh file-type icons after installing/uninstalling the associations.
ChangesAssociations=yes
; Close a running ASMForge when upgrading.
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "associate"; Description: "Open .asm and .pseudo files with ASMForge"; GroupDescription: "File types:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "MIPS assembly IDE and simulator"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; ASMForge appears in "Open with" for these types even when it is not the default.
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".asm"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".pseudo"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""

; .asm -> ASMForge (when the "File types" box is ticked).
Root: HKA; Subkey: "Software\Classes\.asm"; ValueType: string; ValueName: ""; ValueData: "ASMForge.asm"; Flags: uninsdeletevalue; Tasks: associate
Root: HKA; Subkey: "Software\Classes\.asm\OpenWithProgids"; ValueType: string; ValueName: "ASMForge.asm"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associate
Root: HKA; Subkey: "Software\Classes\ASMForge.asm"; ValueType: string; ValueName: ""; ValueData: "MIPS Assembly Source"; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\Classes\ASMForge.asm\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\ASMForge.asm\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: associate

; .pseudo -> ASMForge.
Root: HKA; Subkey: "Software\Classes\.pseudo"; ValueType: string; ValueName: ""; ValueData: "ASMForge.pseudo"; Flags: uninsdeletevalue; Tasks: associate
Root: HKA; Subkey: "Software\Classes\.pseudo\OpenWithProgids"; ValueType: string; ValueName: "ASMForge.pseudo"; ValueData: ""; Flags: uninsdeletevalue; Tasks: associate
Root: HKA; Subkey: "Software\Classes\ASMForge.pseudo"; ValueType: string; ValueName: ""; ValueData: "ASMForge Pseudocode"; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\Classes\ASMForge.pseudo\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\ASMForge.pseudo\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: associate

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
