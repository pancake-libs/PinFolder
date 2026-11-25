@echo off
setlocal
chcp 932 > nul
rem ============================================================
rem 概要    : PinFolder.exe を作る（Windows 標準の csc.exe。Visual Studio は要らない）
rem           src\*.cs をまとめて bin\PinFolder.exe を作り、icon\PinFolder.ico と丸いボタンの絵 icon\float_magnet.png を埋め込む
rem 引数    : なし（ダブルクリックで実行する）
rem 終了コード: 0 = 成功 / 1 = ビルドの誤り / 2 = csc.exe が見つからない
rem ログ    : logs\build_YYYYMMDD_HHMMSS.log
rem 無人実行: 環境変数 NO_PAUSE=1 で最後の pause を省く
rem ============================================================

set "BASE=%~dp0"
set "SRC=%BASE%src"
set "OUT=%BASE%bin"
set "ICON=%BASE%icon\PinFolder.ico"
set "FLOATIMG=%BASE%icon\float_magnet.png"
set "LOGDIR=%BASE%logs"

rem csc.exe の場所（64bit を先に探し、無ければ 32bit）
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

rem ログのファイル名（日時は PowerShell で取る。地域の設定で %DATE% の形が変わるため）
for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd_HHmmss"') do set "STAMP=%%i"
if not exist "%LOGDIR%" mkdir "%LOGDIR%"
set "LOG=%LOGDIR%\build_%STAMP%.log"

echo PinFolder のビルドを始めます > "%LOG%"
echo PinFolder のビルドを始めます

if not exist "%CSC%" (
    echo [誤り] csc.exe が見つかりません。.NET Framework 4.x が入っているか確かめてください >> "%LOG%"
    echo [誤り] csc.exe が見つかりません。.NET Framework 4.x が入っているか確かめてください
    set "RC=2"
    goto :END
)
if not exist "%ICON%" (
    echo [誤り] アイコンがありません：icon\PinFolder.ico >> "%LOG%"
    echo [誤り] アイコンがありません：icon\PinFolder.ico
    set "RC=1"
    goto :END
)
if not exist "%FLOATIMG%" (
    echo [誤り] 丸いボタンの絵がありません：icon\float_magnet.png >> "%LOG%"
    echo [誤り] 丸いボタンの絵がありません：icon\float_magnet.png
    set "RC=1"
    goto :END
)
if not exist "%OUT%" mkdir "%OUT%"

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /warn:4 ^
  /out:"%OUT%\PinFolder.exe" ^
  /win32icon:"%ICON%" /win32manifest:"%SRC%\app.manifest" ^
  /resource:"%ICON%",PinFolder.ico /resource:"%FLOATIMG%",float_magnet.png /resource:"%BASE%icon\float_parts\magnet.png",float_part_magnet.png /resource:"%BASE%icon\float_parts\window_card.png",float_part_window.png ^
  /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:Microsoft.CSharp.dll ^
  "%SRC%\*.cs" >> "%LOG%" 2>&1
if errorlevel 1 (
    echo [誤り] ビルドに失敗しました。ログ：%LOG%
    type "%LOG%"
    set "RC=1"
    goto :END
)

echo ビルドしました：%OUT%\PinFolder.exe >> "%LOG%"
echo ビルドしました：%OUT%\PinFolder.exe
set "RC=0"

:END
if exist "%LOG%" echo 終了コード：%RC% >> "%LOG%"
echo 終了コード：%RC%
if not "%NO_PAUSE%"=="1" pause
exit /b %RC%
