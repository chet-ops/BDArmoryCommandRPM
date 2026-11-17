@echo off
setlocal
REM ============================================================
REM  Builds BDArmoryCommandRPM, installs it into KSP and creates the
REM  release zip in the "release" folder.
REM  Usage:   build.bat "C:\path\to\Kerbal Space Program"
REM  Without an argument it uses the KSP folder that contains this
REM  repository (if the repository is inside the KSP folder).
REM  Requires the .NET SDK (https://dotnet.microsoft.com/download)
REM ============================================================

set "KSP=%~1"
if "%KSP%"=="" if exist "%~dp0..\GameData\Squad" for %%I in ("%~dp0..") do set "KSP=%%~fI"
if "%KSP%"=="" (
  echo [ERROR] KSP folder not found. Usage: build.bat "C:\path\to\Kerbal Space Program"
  pause
  exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERROR] "dotnet" not found. Install the .NET SDK 8 or newer.
  pause
  exit /b 1
)

echo Building against: %KSP%
dotnet build "%~dp0Source\BDArmoryCommandRPM.csproj" -c Release -p:KSPDir="%KSP%" -p:InstallToKSP=true -p:Package=true
if errorlevel 1 (
  echo.
  echo [ERROR] Build failed. Check the messages above.
  pause
  exit /b 1
)

echo.
echo Done. Installed to "%KSP%\GameData\BDArmoryCommandRPM"
echo Release zip is in "%~dp0release"
pause
