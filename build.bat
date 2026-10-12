@echo off
REM Builds PenButton.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
REM Double-click this file. Then run PenButton.exe once: it creates PenClick.exe,
REM PenDoubleClick.exe and PenHold.exe next to itself for the pen-button shortcuts.

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
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /lib:"%FW%WPF" /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll ^
  /win32icon:src\PenButton.ico ^
  /resource:src\SettingsWindow.xaml,SettingsWindow.xaml ^
  /resource:src\PenButton.ico,PenButton.ico ^
  /out:PenButton.exe ^
  src\Program.cs src\Core.cs src\PenButton.cs src\PenButtonSettings.cs
if errorlevel 1 goto fail

echo.
echo Done: PenButton.exe is in %~dp0
echo Run it once; it creates the three button programs next to itself.
if "%CI%"=="" pause
exit /b 0

:fail
echo Build failed. See the messages above.
if "%CI%"=="" pause
exit /b 1
