; Build with Inno Setup 6 after publishing the application to publish\release:
; dotnet publish src\ImmichUploaderApp\ImmichUploaderApp.csproj -c Release -o publish\release
#define MyAppName "Immich Uploader"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "lafkato"
#define MyAppExeName "ImmichUploader.exe"

[Setup]
AppId={{A9CBA759-8C30-42B7-A6CD-079BAA68F080}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppMutex=Local\ImmichUploaderApp_SingleInstance_9F3D2C11
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=..\dist
OutputBaseFilename=ImmichUploaderSetup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\LICENSE
CloseApplications=yes
RestartApplications=yes

[Languages]
Name: "fi"; MessagesFile: "compiler:Languages\Finnish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "sv"; MessagesFile: "compiler:Languages\Swedish.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
fi.CreateDesktopIcon=Luo pikakuvake työpöydälle
fi.LaunchProgram=Käynnistä %1
en.CreateDesktopIcon=Create a desktop shortcut
en.LaunchProgram=Launch %1
sv.CreateDesktopIcon=Skapa en genväg på skrivbordet
sv.LaunchProgram=Starta %1
de.CreateDesktopIcon=Desktop-Verknüpfung erstellen
de.LaunchProgram=%1 starten

[Files]
Source: "..\publish\release\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "Additional icons:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
