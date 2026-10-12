@echo off
REM Builds PenButton.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
REM Double-click this file. Then run PenButton.exe once: it writes PenClick.exe,
REM PenDoubleClick.exe and PenHold.exe (small, grey icon) next to itself for the pen shortcuts.

setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo csc.exe not found. Please enable .NET Framework 4.x in Windows Features.
    goto fail
)
for %%I in ("%CSC%") do set "FW=%%~dpI"
if not exist obj mkdir obj

REM /codepage:65001 = the sources are UTF-8 (they contain Chinese text).

REM 1. The small button program (no window, grey icon). It is embedded in PenButton.exe.
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /win32icon:src\PenGesture.ico ^
  /out:obj\PenGesture.exe ^
  src\GestureProgram.cs src\Core.cs src\PenButton.cs
if errorlevel 1 goto fail

REM 2. PenButton.exe: settings window + the program above as a resource.
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /lib:"%FW%WPF" /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll ^
  /r:System.Web.Extensions.dll ^
  /win32icon:src\PenButton.ico ^
  /resource:src\SettingsWindow.xaml,SettingsWindow.xaml ^
  /resource:src\PenButton.ico,PenButton.ico ^
  /resource:obj\PenGesture.exe,PenGesture.exe ^
  /out:PenButton.exe ^
  src\Program.cs src\Core.cs src\PenButton.cs src\PenButtonSettings.cs src\Updater.cs
if errorlevel 1 goto fail

rmdir /s /q obj
echo.
echo Done: PenButton.exe is in %~dp0
echo Run it once; it writes the three button programs next to itself.
if "%CI%"=="" pause
exit /b 0

:fail
echo Build failed. See the messages above.
if "%CI%"=="" pause
exit /b 1
