; Basic installer script for GeFeSLE Server
!include "MUI2.nsh"
!include "VersionCompare.nsh"

; Update ALL version references (change these for each version)
!define VERSION "0.0.9"

; General Settings  
Name "GeFeSLE Server"
OutFile "GeFeSLE-Server-${VERSION}-setup.exe"
InstallDir "$PROGRAMFILES\GeFeSLE-Server"
InstallDirRegKey HKCU "Software\GeFeSLE-Server" ""
RequestExecutionLevel admin

; Version Information
VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "GeFeSLE Server"
VIAddVersionKey "FileDescription" "GeFeSLE - Generic, Federated, Subscribable List Engine - Server"
VIAddVersionKey "LegalCopyright" "© tezoatlipoca@gmail.com"
VIAddVersionKey "FileVersion" "${VERSION}"

; Interface Settings
!define MUI_ABORTWARNING
!define MUI_ICON "gefesleff.ico" ; Optional: Replace with your icon path

; Pages
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

; Languages
!insertmacro MUI_LANGUAGE "English"

; Installer Sections
Section "Install"
  SetOutPath "$INSTDIR"

  ; Add files - copy all files from publish folder
  File "..\bin\Release\net8.0\win-x64\publish\GeFeSLE.exe"
  File "..\bin\Release\net8.0\win-x64\publish\*.dll"
  File "..\bin\Release\net8.0\win-x64\publish\appsettings.json"
  File "..\bin\Release\net8.0\win-x64\publish\config.SAMPLE.json"
  
  ; Add service installation scripts
  File "install-service.ps1"
  File "uninstall-service.ps1"
  File "path.ps1"

  ; Check for existing version
  ReadRegStr $R0 HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\GeFeSLE-Server" "DisplayVersion"
  
  StrCpy $R1 "${VERSION}" ; This installer's version

  ${If} $R0 != ""
    ${VersionCompare} $R0 $R1 $R2
    ${If} $R2 == "1" ; R0 > R1 (installed version is newer)
      MessageBox MB_ICONSTOP "A newer version ($R0) of GeFeSLE Server is already installed.$\nCurrent installer: $R1$\nInstaller will now close."
      Quit
    ${ElseIf} $R2 == "0" ; R0 == R1 (same version)
      MessageBox MB_ICONQUESTION|MB_YESNO "GeFeSLE Server version $R0 is already installed. Do you want to reinstall?" IDNO quit_installer
    ${Else}
      MessageBox MB_ICONQUESTION|MB_YESNO "Upgrading GeFeSLE Server from version $R0 to ${VERSION}. Continue?" IDNO quit_installer
    ${EndIf}
  ${EndIf}

  Goto continue_install
  
  quit_installer:
    Quit
  
  continue_install:

  ; Create Start Menu shortcuts
  CreateDirectory "$SMPROGRAMS\GeFeSLE Server"
  CreateShortcut "$SMPROGRAMS\GeFeSLE Server\GeFeSLE Server.lnk" "$INSTDIR\GeFeSLE.exe"
  CreateShortcut "$SMPROGRAMS\GeFeSLE Server\Install as Windows Service.lnk" "powershell.exe" "-ExecutionPolicy Bypass -File $\"$INSTDIR\install-service.ps1$\""
  CreateShortcut "$SMPROGRAMS\GeFeSLE Server\Uninstall Service.lnk" "powershell.exe" "-ExecutionPolicy Bypass -File $\"$INSTDIR\uninstall-service.ps1$\""
  CreateShortcut "$SMPROGRAMS\GeFeSLE Server\Configuration Folder.lnk" "$INSTDIR"
  CreateShortcut "$SMPROGRAMS\GeFeSLE Server\Uninstall.lnk" "$INSTDIR\uninstall.exe"

  ; Create uninstaller
  WriteUninstaller "$INSTDIR\uninstall.exe"

  ; Registry entries for uninstaller
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\GeFeSLE-Server" "DisplayName" "GeFeSLE Server"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\GeFeSLE-Server" "UninstallString" "$\"$INSTDIR\uninstall.exe$\""
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\GeFeSLE-Server" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\GeFeSLE-Server" "Publisher" "tezoatlipoca"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\GeFeSLE-Server" "InstallLocation" "$INSTDIR"
  
  ; Installation complete messages
  DetailPrint "GeFeSLE Server installed successfully"
  DetailPrint "You can run the server from the Start Menu or by executing GeFeSLE.exe in $INSTDIR"
  DetailPrint "To install as a Windows Service, use the 'Install as Windows Service' shortcut in the Start Menu"
  DetailPrint "Default server URL will be: http://localhost:5000"
  DetailPrint "Note: This version includes built-in Windows Service support"
SectionEnd

; Uninstaller Section
Section "Uninstall"
  ; Stop and remove service if it exists
  nsExec::ExecToLog 'powershell.exe -ExecutionPolicy Bypass -Command "if (Get-Service -Name GeFeSLE-Server -ErrorAction SilentlyContinue) { Stop-Service -Name GeFeSLE-Server -Force; sc.exe delete GeFeSLE-Server }"'
  
  ; Remove files and folders
  Delete "$INSTDIR\GeFeSLE.exe"
  Delete "$INSTDIR\*.dll"
  Delete "$INSTDIR\appsettings.json"
  Delete "$INSTDIR\config.SAMPLE.json"
  Delete "$INSTDIR\install-service.ps1"
  Delete "$INSTDIR\uninstall-service.ps1"
  Delete "$INSTDIR\path.ps1"
  Delete "$INSTDIR\uninstall.exe"
  
  ; Remove shortcuts
  Delete "$SMPROGRAMS\GeFeSLE Server\GeFeSLE Server.lnk"
  Delete "$SMPROGRAMS\GeFeSLE Server\Install as Windows Service.lnk"
  Delete "$SMPROGRAMS\GeFeSLE Server\Uninstall Service.lnk"
  Delete "$SMPROGRAMS\GeFeSLE Server\Configuration Folder.lnk"
  Delete "$SMPROGRAMS\GeFeSLE Server\Uninstall.lnk"
  RMDir "$SMPROGRAMS\GeFeSLE Server"
  
  ; Remove directories
  RMDir "$INSTDIR"
  
  ; Remove registry keys
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\GeFeSLE-Server"
  
  DetailPrint "GeFeSLE Server has been uninstalled"
SectionEnd