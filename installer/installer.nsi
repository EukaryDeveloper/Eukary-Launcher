; ============================================================================
;  Eukary Launcher（真核启动器）NSIS 安装脚本
;  产出：单个 exe 安装包，内含自包含 .NET 8 单文件程序，无需预装运行时
;
;  请用 installer\build_installer.ps1 构建，它会先 publish 再调 makensis。
;  手动构建示例：
;    makensis.exe /DVERSION=0.1.0 /DPUBLISH_DIR="<abs>\publish" installer.nsi
; ============================================================================

Unicode true

; ---- 可在命令行用 /D 覆盖的参数 --------------------------------------------
!ifndef VERSION
  !define VERSION       "0.1.0"
!endif
!ifndef PUBLISH_DIR
  !define PUBLISH_DIR   "..\EukaryLauncher.App\bin\Release\net8.0-windows\win-x64\publish"
!endif
!ifndef DIST_DIR
  !define DIST_DIR      "..\dist"
!endif
!ifndef REPO_ROOT
  !define REPO_ROOT     ".."
!endif

; ---- 应用元信息 -------------------------------------------------------------
!define APP_NAME        "Eukary Launcher"
!define APP_NAME_CN     "真核启动器"
!define APP_DESC        "免费，开源，个性化的 Minecraft Java 版第三方启动器"
!define APP_EXE         "EukaryLauncher.exe"
!define PROC_NAME       "EukaryLauncher"
!define PUBLISHER       "DL_Hachi"
!define UNINST_EXE      "uninstall.exe"
!define UNINST_KEY      "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"
!define DEFAULT_STARTMENU_FOLDER "${APP_NAME}"

; 用户数据（游戏本体 / Java 运行时）目录，卸载时可选择是否清理
!define USER_DATA_DIR   "$LOCALAPPDATA\EukaryLauncher"

; ---- 输出 -------------------------------------------------------------------
Name              "${APP_NAME_CN} ${APP_NAME}"
OutFile           "${DIST_DIR}\EukaryLauncher-${VERSION}-Setup.exe"
InstallDir        "$LOCALAPPDATA\Programs\${APP_NAME}"
InstallDirRegKey  HKCU "${UNINST_KEY}" "InstallLocation"
RequestExecutionLevel user          ; 按用户安装，不需要管理员权限
SetCompressor     /SOLID lzma       ; 最大压缩率
SetCompressorDictSize 64
SetOverwrite      ifnewer
AllowSkipFiles    off
AutoCloseWindow   false
ShowInstDetails   show
ShowUninstDetails show
BrandingText      "${APP_NAME} · ${APP_DESC}"

VIAddVersionKey "ProductName"     "${APP_NAME_CN} ${APP_NAME}"
VIAddVersionKey "ProductVersion"  "${VERSION}"
VIAddVersionKey "FileDescription" "${APP_NAME} 安装程序"
VIAddVersionKey "FileVersion"     "${VERSION}"
VIAddVersionKey "CompanyName"     "${PUBLISHER}"
VIAddVersionKey "LegalCopyright"  "Copyright (C) ${PUBLISHER} · GPL-3.0"
VIProductVersion "${VERSION}.0"

; ---- 界面 -------------------------------------------------------------------
!define MUI_ICON              "${REPO_ROOT}\EukaryLauncher.App\Assets\AppIcon.ico"
!define MUI_UNICON            "${REPO_ROOT}\EukaryLauncher.App\Assets\AppIcon.ico"
!define MUI_ABORTWARNING
!define MUI_UNABORTWARNING
!define MUI_FINISHPAGE_RUN             "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT        "立即启动 ${APP_NAME_CN}"
!define MUI_FINISHPAGE_SHOWREADME_TEXT "查看许可协议"
!define MUI_FINISHPAGE_LINK            "GNU GPL-3.0"
!define MUI_FINISHPAGE_LINK_LOCATION   "https://www.gnu.org/licenses/gpl-3.0.html"

; 开始菜单文件夹记录位置（卸载时靠它找回当时选择的文件夹名）
!define MUI_STARTMENUPAGE_REGISTRY_ROOT   "HKCU"
!define MUI_STARTMENUPAGE_REGISTRY_KEY    "Software\${APP_NAME}"
!define MUI_STARTMENUPAGE_REGISTRY_VALUENAME "StartMenuFolder"

!include "MUI2.nsh"
!include "WinVer.nsh"       ; ${AtMostWin8.1} 需要

Var StartMenuFolder

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${REPO_ROOT}\installer\LICENSE.txt"
!define MUI_COMPONENTSPAGE_TEXT_TOP "$(TEXT_IO_TITLE)"
!define MUI_COMPONENTSPAGE_TEXT_COMPLIST "$(TEXT_IO_SUBTITLE)"
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_STARTMENU "Application" $StartMenuFolder
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

!insertmacro MUI_LANGUAGE "SimpChinese"
LangString TEXT_IO_TITLE      ${LANG_SIMPCHINESE} "安装选项"
LangString TEXT_IO_SUBTITLE   ${LANG_SIMPCHINESE} "选择要安装的功能，鼠标悬停可查看说明。"
LangString TEXT_UNINST_DATA    ${LANG_SIMPCHINESE} "是否同时删除已下载的游戏文件与 Java 运行时？$\n$\n保留可以让你重装后不必重新下载（可能占用数 GB）。"

; ============================================================================
;  初始化
; ============================================================================

Function .onInit
  StrCpy $StartMenuFolder "${DEFAULT_STARTMENU_FOLDER}"

  ; .NET 8 要求 Windows 10 1607 / Windows Server 2016 及以上
  ${If} ${AtMostWin8.1}
    MessageBox MB_OK|MB_ICONSTOP \
      "${APP_NAME_CN} 需要 Windows 10 或更高版本的操作系统。$\n$\n当前系统版本过低，无法安装。"
    Abort
  ${EndIf}

  ; 若程序正在运行，要求先退出，否则 exe 会被占用而覆盖失败
  Call DetectAndAskClose
FunctionEnd

; 检测进程，提示用户关闭（$0=0 未运行 / 1 运行中 / error 超时）
Function DetectAndAskClose
  CheckAgain:
    nsExec::ExecToStack /TIMEOUT=5000 \
      'powershell -NoProfile -Command "if (Get-Process -Name ${PROC_NAME} -ErrorAction SilentlyContinue) { exit 1 } exit 0"'
    Pop $0
    Pop $1
    ${If} $0 == "1"
      MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION \
        "检测到 ${APP_NAME_CN} 正在运行。$\n$\n请先关闭它，然后点击“重试”继续安装。" \
        IDRETRY CheckAgain
      Abort
    ${EndIf}
FunctionEnd

; ============================================================================
;  安装
; ============================================================================

Section "主程序" SEC_MAIN
  SectionIn RO
  SetOutPath "$INSTDIR"

  DetailPrint "正在复制主程序文件…"
  File "${PUBLISH_DIR}\${APP_EXE}"
  File "${PUBLISH_DIR}\*.dll"

  DetailPrint "正在写入卸载信息…"
  WriteUninstaller "$INSTDIR\${UNINST_EXE}"

  WriteRegStr   HKCU "${UNINST_KEY}" "DisplayName"     "${APP_NAME_CN} ${APP_NAME}"
  WriteRegStr   HKCU "${UNINST_KEY}" "DisplayVersion"  "${VERSION}"
  WriteRegStr   HKCU "${UNINST_KEY}" "Publisher"       "${PUBLISHER}"
  WriteRegStr   HKCU "${UNINST_KEY}" "DisplayIcon"     "$INSTDIR\${APP_EXE},0"
  WriteRegStr   HKCU "${UNINST_KEY}" "UninstallString" "$INSTDIR\${UNINST_EXE}"
  WriteRegStr   HKCU "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\${UNINST_EXE}" /S'
  WriteRegStr   HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr   HKCU "${UNINST_KEY}" "HelpLink"        "https://www.gnu.org/licenses/gpl-3.0.html"
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify"        1
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair"        1
  ; 估算体积（KB），用于“添加/删除程序”列表显示
  WriteRegDWORD HKCU "${UNINST_KEY}" "EstimatedSize"   68000

  WriteRegStr HKCU "Software\${APP_NAME}" "InstallPath" "$INSTDIR"
  WriteRegStr HKCU "Software\${APP_NAME}" "Version"     "${VERSION}"

  !insertmacro MUI_STARTMENU_WRITE_BEGIN "Application"
    CreateDirectory "$SMPROGRAMS\$StartMenuFolder"
    CreateShortCut  "$SMPROGRAMS\$StartMenuFolder\${APP_NAME_CN}.lnk" "$INSTDIR\${APP_EXE}"
    CreateShortCut  "$SMPROGRAMS\$StartMenuFolder\卸载 ${APP_NAME_CN}.lnk" "$INSTDIR\${UNINST_EXE}"
  !insertmacro MUI_STARTMENU_WRITE_END
SectionEnd

Section "桌面快捷方式" SEC_DESKTOP
  CreateShortCut "$DESKTOP\${APP_NAME_CN}.lnk" "$INSTDIR\${APP_EXE}"
SectionEnd

; 中文 section 描述
LangString DESC_SEC_MAIN    ${LANG_SIMPCHINESE} "启动器主程序与运行所需组件（必需）"
LangString DESC_SEC_DESKTOP ${LANG_SIMPCHINESE} "在桌面创建快捷方式"
!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_MAIN}    $(DESC_SEC_MAIN)
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_DESKTOP} $(DESC_SEC_DESKTOP)
!insertmacro MUI_FUNCTION_DESCRIPTION_END

; ============================================================================
;  卸载
; ============================================================================

Function un.onInit
  ; 静默卸载时跳过确认（/SD 保证 MessageBox 不会挂起卸载流程）
  MessageBox MB_OKCANCEL|MB_ICONINFORMATION \
    "即将卸载 ${APP_NAME_CN} ${APP_NAME}。" /SD IDOK IDOK Cont
  Abort
  Cont:
FunctionEnd

Section "Uninstall"
  ; 卸载前也要确保程序没在运行
  Call un.DetectAndAskClose

  SetShellVarContext current

  DetailPrint "正在删除快捷方式…"
  !insertmacro MUI_STARTMENU_GETFOLDER "Application" $StartMenuFolder
  Delete "$SMPROGRAMS\$StartMenuFolder\${APP_NAME_CN}.lnk"
  Delete "$SMPROGRAMS\$StartMenuFolder\卸载 ${APP_NAME_CN}.lnk"
  RMDir  "$SMPROGRAMS\$StartMenuFolder"
  Delete "$DESKTOP\${APP_NAME_CN}.lnk"

  DetailPrint "正在删除程序文件…"
  Delete "$INSTDIR\${APP_EXE}"
  Delete "$INSTDIR\*.dll"
  Delete "$INSTDIR\${UNINST_EXE}"
  RMDir /r "$INSTDIR\AppData"        ; 仅清理程序自带的临时解压目录（若存在）
  Delete "$INSTDIR\README.txt"
  RMDir  "$INSTDIR"

  DetailPrint "正在清理注册表…"
  DeleteRegKey HKCU "${UNINST_KEY}"
  DeleteRegKey HKCU "Software\${APP_NAME}"

  ; 用户数据默认保留，询问后再决定是否删除（静默卸载绝不删数据）
  IfSilent SkipData
  MessageBox MB_YESNO|MB_ICONQUESTION "$(TEXT_UNINST_DATA)" /SD IDNO IDNO SkipData
  DetailPrint "正在删除用户数据 ${USER_DATA_DIR}…"
  RMDir /r "${USER_DATA_DIR}"
  SkipData:

  SetAutoClose false
SectionEnd

Function un.DetectAndAskClose
  ; 静默卸载跳过进程检测（CI / 无人值守场景，直接删，删不掉由 /REBOOTOK 兜底）
  IfSilent DetectDone
  UnCheckAgain:
    nsExec::ExecToStack /TIMEOUT=5000 \
      'powershell -NoProfile -Command "if (Get-Process -Name ${PROC_NAME} -ErrorAction SilentlyContinue) { exit 1 } exit 0"'
    Pop $0
    Pop $1
    ${If} $0 == "1"
      MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION \
        "检测到 ${APP_NAME_CN} 正在运行。$\n$\n请先关闭它，然后点击“重试”继续卸载。" \
        /SD IDCANCEL IDRETRY UnCheckAgain
      Abort
    ${EndIf}
  DetectDone:
FunctionEnd
