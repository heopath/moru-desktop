@echo off
setlocal
cd /d "%~dp0"
if exist "artifacts\Moru-win-x64\Moru.exe" (
  start "" "artifacts\Moru-win-x64\Moru.exe"
  exit /b 0
)
if exist ".tools\dotnet\dotnet.exe" set "DOTNET_ROOT=%~dp0.tools\dotnet"
if exist "src\Moru\bin\Release\net10.0-windows\Moru.exe" (
  start "" "src\Moru\bin\Release\net10.0-windows\Moru.exe"
  exit /b 0
)
echo Build first: powershell -File build.ps1 -Portable
pause
