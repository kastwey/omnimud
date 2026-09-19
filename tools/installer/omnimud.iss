; Omnimud installer (Inno Setup 6).
;
; Built by tools\release.ps1:
;   ISCC /DAppVersion=2.0.0 /DSourceDir=<published folder> /DOutputDir=<where to leave the setup> omnimud.iss
;
; Design:
;  * PER-USER install in %LocalAppData%\Programs\Omnimud, WITHOUT administrator rights. Omnimud is portable: it
;    keeps everything (database, logs, downloaded sounds, master key) in a "data" folder next to Omnimud.exe, so
;    the program folder must stay writable for the user. Program Files would break that.
;  * It installs exactly the portable build: the same files as the zip.
;  * The "data" folder is never created, touched or packaged by the installer.
;  * The uninstaller ASKS before deleting "data" (default: keep it). A silent uninstall always keeps it.
;  * Spanish and English, like the program.
;  * Nothing is downloaded, no registry keys beyond the standard uninstall entry of Inno Setup (HKCU).

#ifndef AppVersion
  #error Pass /DAppVersion=x.y.z (tools\release.ps1 does it)
#endif
#ifndef SourceDir
  #error Pass /DSourceDir=<folder with the published files>
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
; Never change AppId: it is how an update finds the previous installation.
AppId={{6B0C2F0E-5B7A-4E0B-9C57-4F4D6E6D7A21}
AppName=Omnimud
AppVersion={#AppVersion}
AppVerName=Omnimud {#AppVersion}
AppPublisher=Juanjo Montiel
AppPublisherURL=https://github.com/kastwey/omnimud
AppSupportURL=https://github.com/kastwey/omnimud/issues
AppUpdatesURL=https://github.com/kastwey/omnimud/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\Omnimud
DefaultGroupName=Omnimud
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=
UsePreviousAppDir=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.16299
LicenseFile={#SourceDir}\LICENSE.txt
OutputDir={#OutputDir}
OutputBaseFilename=Omnimud-{#AppVersion}-win-x64-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\Omnimud.exe
UninstallDisplayName=Omnimud {#AppVersion}
ShowLanguageDialog=auto

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
en.DesktopIcon=Create a shortcut on the &desktop
es.DesktopIcon=Crear un acceso directo en el &escritorio
en.LaunchProgram=Start Omnimud now
es.LaunchProgram=Iniciar Omnimud ahora
en.DeleteData=Do you also want to delete your Omnimud data (MUDs, characters, aliases, triggers, options, logs and downloaded sounds)?%n%nThey are in:%n%1%n%nChoose No to keep them: you can use them again if you reinstall Omnimud, or copy that folder elsewhere.
es.DeleteData=¿Quieres borrar también tus datos de Omnimud (MUD, personajes, alias, triggers, opciones, registros y sonidos descargados)?%n%nEstán en:%n%1%n%nElige No para conservarlos: podrás volver a usarlos si reinstalas Omnimud, o copiar esa carpeta a otro sitio.
en.DataKept=Your data has been kept in:%n%1
es.DataKept=Tus datos se han conservado en:%n%1

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked

[Files]
; Everything the portable build has. "data" is excluded in case the source folder was ever run from.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "\data\*,*.pdb,*.db,*.db-wal,*.db-shm,master.key"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Omnimud"; Filename: "{app}\Omnimud.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Omnimud"; Filename: "{app}\Omnimud.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Omnimud.exe"; Description: "{cm:LaunchProgram}"; Flags: nowait postinstall skipifsilent unchecked

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep <> usPostUninstall then
    exit;

  DataDir := ExpandConstant('{app}\data');
  if not DirExists(DataDir) then
    exit;

  { A silent uninstall cannot ask, so it never deletes the user's data. The default button is No. }
  if (not UninstallSilent) and
     (MsgBox(FmtMessage(CustomMessage('DeleteData'), [DataDir]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
  begin
    DelTree(DataDir, True, True, True);
    RemoveDir(ExpandConstant('{app}'));
  end
  else if not UninstallSilent then
    MsgBox(FmtMessage(CustomMessage('DataKept'), [DataDir]), mbInformation, MB_OK);
end;
