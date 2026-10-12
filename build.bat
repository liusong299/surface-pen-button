@echo off
REM Builds PenButton with the C# compiler that ships with Windows (.NET Framework 4.x).
REM Double-click this file. It creates, next to itself:
REM   PenClick.exe, PenDoubleClick.exe, PenHold.exe   (bind these to the pen button)
REM   PenButtonSettings.exe                            (the settings window)

setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo csc.exe not found. Please enable .NET Framework 4.x in Windows Features.
    goto fail
)

for %%I in ("%CSC%") do set "FW=%%~dpI"

REM /codepage:65001 = the sources are UTF-8 (they contain Chinese text).
set "OPTS=/nologo /target:winexe /optimize+ /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll"
set "WPF=/lib:"%FW%WPF" /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll"

"%CSC%" %OPTS% /out:PenClick.exe src\PenButton.cs src\Core.cs
if errorlevel 1 goto fail

REM The same program under three names: it reads its own name to know the gesture.
copy /y PenClick.exe PenDoubleClick.exe >nul
if errorlevel 1 goto fail
copy /y PenClick.exe PenHold.exe >nul
if errorlevel 1 goto fail

"%CSC%" %OPTS% %WPF% /resource:src\SettingsWindow.xaml,SettingsWindow.xaml /out:PenButtonSettings.exe src\PenButtonSettings.cs src\Core.cs
if errorlevel 1 goto fail

echo.
echo Done: PenClick.exe, PenDoubleClick.exe, PenHold.exe and PenButtonSettings.exe are in %~dp0
if "%CI%"=="" pause
exit /b 0

:fail
echo Build failed. See the messages above.
if "%CI%"=="" pause
exit /b 1
