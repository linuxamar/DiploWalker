!include "MUI2.nsh"
!include "WinVer.nsh"

; ── Définitions par défaut (surchargeables via -D) ──────────────────────────
!ifndef APP_VERSION
  !define APP_VERSION "1.0.0"
!endif

!ifndef PUBLISH_ROOT
  !define PUBLISH_ROOT "..\publish\WindowsServices\x64"
!endif

!ifndef APP_NAME
  !define APP_NAME "Diplo"
!endif

!define REG_KEY_UNINSTALL "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"
!define REG_KEY_ENV "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"

; ── Attributs ───────────────────────────────────────────────────────────────
Name "${APP_NAME}"
OutFile "Diplo-Setup-${APP_VERSION}.exe"
InstallDir "$PROGRAMFILES64\${APP_NAME}"
InstallDirRegKey HKLM "${REG_KEY_UNINSTALL}" "InstallDir"
RequestExecutionLevel admin
BrandingText "Diplo"

; ── Interface MUI ───────────────────────────────────────────────────────────
!define MUI_ABORTWARNING
!define MUI_ICON "${NSISDIR}\Contrib\Graphics\Icons\modern-install.ico"
!define MUI_UNICON "${NSISDIR}\Contrib\Graphics\Icons\modern-uninstall.ico"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\LICENSE"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "French"

; ── Fonctions PATH (macro pour installer + désinstallateur) ─────────────────

!macro AddToPathFunc UN
Function ${UN}AddToPath
  Exch $R0
  Push $R1

  ReadRegStr $R1 HKLM "${REG_KEY_ENV}" "Path"
  ${If} $R1 == ""
    StrCpy $R1 "$R0"
  ${Else}
    StrCpy $R1 "$R1;$R0"
  ${EndIf}
  WriteRegExpandStr HKLM "${REG_KEY_ENV}" "Path" "$R1"
  SendMessage ${HWND_BROADCAST} ${WM_SETTINGCHANGE} 0 "STR:Environment"
  DetailPrint "PATH += $R0"

  Pop $R1
  Pop $R0
FunctionEnd
!macroend
!insertmacro AddToPathFunc ""
!insertmacro AddToPathFunc "un."

!macro StrStrFunc UN
Function ${UN}StrStr
  Exch $R0
  Exch 1
  Exch $R1
  Push $R2
  Push $R3
  Push $R4

  StrLen $R2 $R0
  ${If} $R2 <= 0
    StrCpy $R1 ""
    Goto s_done
  ${EndIf}
  StrLen $R3 $R1
  ${If} $R2 > $R3
    StrCpy $R1 ""
    Goto s_done
  ${EndIf}

  IntOp $R3 $R3 - $R2
  ${For} $R2 0 $R3
    StrCpy $R4 $R1 $R0 $R2
    ${If} $R4 == $R0
      StrCpy $R1 $R1 "" $R2
      Goto s_done
    ${EndIf}
  ${Next}
  StrCpy $R1 ""

s_done:
  Pop $R4
  Pop $R3
  Pop $R2
  Exch $R1
  Pop $R0
FunctionEnd
!macroend
!insertmacro StrStrFunc ""
!insertmacro StrStrFunc "un."

!macro RemoveFromPathFunc UN
Function ${UN}RemoveFromPath
  Exch $R0
  Push $R1
  Push $R2
  Push $R3

  ReadRegStr $R1 HKLM "${REG_KEY_ENV}" "Path"
  ${If} $R1 == ""
    Goto r_done
  ${EndIf}

  StrCpy $R2 ";$R1;"
  StrCpy $R3 ";$R0;"

  Push $R2
  Push $R3
  Call ${UN}StrStr
  Pop $R1
  ${If} $R1 == ""
    DetailPrint "Introuvable dans PATH : $R0"
    Goto r_done
  ${EndIf}

  StrCpy $R2 $R1 "" 0
  StrLen $R3 $R3
  StrCpy $R2 $R2 "" $R3

  WriteRegExpandStr HKLM "${REG_KEY_ENV}" "Path" "$R2"
  SendMessage ${HWND_BROADCAST} ${WM_SETTINGCHANGE} 0 "STR:Environment"
  DetailPrint "PATH -= $R0"

r_done:
  Pop $R3
  Pop $R2
  Pop $R1
  Pop $R0
FunctionEnd
!macroend
!insertmacro RemoveFromPathFunc ""
!insertmacro RemoveFromPathFunc "un."

; ── Sections ────────────────────────────────────────────────────────────────

Section "Diplo.GUI" SecGui
  SectionIn RO
  SetOutPath "$INSTDIR\Diplo.Gui"
  File /r "${PUBLISH_ROOT}\Diplo.Gui\*.*"

  CreateShortCut "$DESKTOP\Diplo GUI.lnk"                "$INSTDIR\Diplo.Gui\Diplo.Gui.exe"
  CreateDirectory  "$SMPROGRAMS\${APP_NAME}"
  CreateShortCut "$SMPROGRAMS\${APP_NAME}\Diplo GUI.lnk" "$INSTDIR\Diplo.Gui\Diplo.Gui.exe"
SectionEnd

Section "Diplo.CLI" SecCli
  SectionIn RO
  SetOutPath "$INSTDIR\Diplo.Cli"
  File /r "${PUBLISH_ROOT}\Diplo.Cli\*.*"

  Push "$INSTDIR\Diplo.Cli"
  Call AddToPath

  CreateShortCut "$SMPROGRAMS\${APP_NAME}\Diplo CLI (cmd).lnk" "%windir%\system32\cmd.exe" "/K $INSTDIR\Diplo.Cli\Diplo.Cli.exe --help"
SectionEnd

Section "Diplo.Installer" SecInst
  SectionIn RO
  SetOutPath "$INSTDIR\Diplo.Installer"
  File /r "${PUBLISH_ROOT}\Diplo.Installer\*.*"
SectionEnd

Section -Additional
  WriteUninstaller "$INSTDIR\uninst.exe"

  WriteRegStr   HKLM "${REG_KEY_UNINSTALL}" "DisplayName"     "${APP_NAME}"
  WriteRegStr   HKLM "${REG_KEY_UNINSTALL}" "DisplayVersion"  "${APP_VERSION}"
  WriteRegStr   HKLM "${REG_KEY_UNINSTALL}" "Publisher"       "Diplo"
  WriteRegStr   HKLM "${REG_KEY_UNINSTALL}" "InstallDir"      "$INSTDIR"
  WriteRegStr   HKLM "${REG_KEY_UNINSTALL}" "UninstallString" "$INSTDIR\uninst.exe"
  WriteRegDWORD HKLM "${REG_KEY_UNINSTALL}" "NoModify"        1
  WriteRegDWORD HKLM "${REG_KEY_UNINSTALL}" "NoRepair"        1
SectionEnd

Function .onInit
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_ICONSTOP "Diplo nécessite Windows 10 ou ultérieur."
    Abort
  ${EndIf}
FunctionEnd

Section "Uninstall"
  Push "$INSTDIR\Diplo.Cli"
  Call un.RemoveFromPath

  Delete "$DESKTOP\Diplo GUI.lnk"
  RMDir  /r "$SMPROGRAMS\${APP_NAME}"

  RMDir  /r "$INSTDIR\Diplo.Gui"
  RMDir  /r "$INSTDIR\Diplo.Cli"
  RMDir  /r "$INSTDIR\Diplo.Installer"
  Delete "$INSTDIR\uninst.exe"
  RMDir  "$INSTDIR"

  DeleteRegKey HKLM "${REG_KEY_UNINSTALL}"
SectionEnd
