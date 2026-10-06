; Build with Inno Setup 6 after publishing the application to publish\release:
; dotnet publish src\ImmichUploaderApp\ImmichUploaderApp.csproj -c Release -o publish\release
#define MyAppName "Immich Uploader"
#ifndef MyAppVersion
  #define MyAppVersion "1.3.1"
#endif
#define MyAppPublisher "lafkato"
#define MyAppExeName "ImmichUploader.exe"

[Setup]
AppId={{A9CBA759-8C30-42B7-A6CD-079BAA68F080}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/lafkato/immich-uploader
AppSupportURL=https://github.com/lafkato/immich-uploader/issues
VersionInfoDescription=Immich Uploader Setup
VersionInfoProductName=Immich Uploader
#ifndef InstallerPreview
AppMutex=Local\ImmichUploaderApp_SingleInstance_9F3D2C11
#endif
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=..\dist
OutputBaseFilename=ImmichUploaderSetup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
SetupIconFile=..\src\ImmichUploaderApp\Resources\icon.ico
WizardStyle=modern dynamic polar includetitlebar
WizardSizePercent=125
WizardImageFile=branding\wizard-sidebar.bmp
WizardSmallImageFile=branding\wizard-icon.png
WizardImageFileDynamicDark=branding\wizard-sidebar.bmp
WizardSmallImageFileDynamicDark=branding\wizard-icon.png
WizardImageStretch=yes
DisableWelcomePage=no
#ifdef InstallerPreview
PrivilegesRequired=lowest
#endif
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
fi.AdditionalIcons=Pikakuvakkeet:
fi.CreateDesktopIcon=Luo pikakuvake työpöydälle
fi.LaunchProgram=Käynnistä %1
en.AdditionalIcons=Shortcuts:
en.CreateDesktopIcon=Create a desktop shortcut
en.LaunchProgram=Launch %1
sv.AdditionalIcons=Genvägar:
sv.CreateDesktopIcon=Skapa en genväg på skrivbordet
sv.LaunchProgram=Starta %1
de.AdditionalIcons=Verknüpfungen:
de.CreateDesktopIcon=Desktop-Verknüpfung erstellen
de.LaunchProgram=%1 starten

fi.WelcomeTagline=Kuvasi, molempiin suuntiin.
fi.WelcomeDetails=Automaattiset lähetykset Immichiin, kuvien ja videoiden vastaanotto sekä selkeä siirtojen seuranta omalla koneellasi.
fi.WelcomeVersion=Asennetaan versio %1.
en.WelcomeTagline=Your photos, both ways.
en.WelcomeDetails=Automatic uploads to Immich, photo and video downloads, and a clear view of your transfers on your computer.
en.WelcomeVersion=Install version %1.
sv.WelcomeTagline=Dina bilder, åt båda håll.
sv.WelcomeDetails=Automatiska uppladdningar till Immich, hämtning av bilder och videor och en tydlig översikt över överföringar på din dator.
sv.WelcomeVersion=Installera version %1.
de.WelcomeTagline=Deine Fotos, in beide Richtungen.
de.WelcomeDetails=Automatische Uploads zu Immich, Downloads von Fotos und Videos und eine klare Übersicht deiner Übertragungen auf deinem Computer.
de.WelcomeVersion=Version %1 installieren.

[Files]
Source: "..\publish\release\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
procedure InitializeWizard();
begin
  WizardForm.WelcomeLabel1.Caption := '{#MyAppName}';
  WizardForm.WelcomeLabel2.Caption := CustomMessage('WelcomeTagline') + #13#10 + #13#10 +
    CustomMessage('WelcomeDetails') + #13#10 + #13#10 +
    FmtMessage(CustomMessage('WelcomeVersion'), ['{#MyAppVersion}']);
end;

#ifdef InstallerPreview
// A preview can be browsed without interrupting the running app, but cannot install it.
procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpReady then WizardForm.NextButton.Enabled := False;
end;
#endif
