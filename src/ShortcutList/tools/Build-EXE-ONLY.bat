@echo off
setlocal

echo BUILD EXE ONLY

cd /d %~dp0\..

dotnet publish -c Release -r win-x64 --self-contained true ^
 /p:PublishSingleFile=true ^
 /p:IncludeNativeLibrariesForSelfExtract=true ^
 /p:EnableCompressionInSingleFile=true ^
 /p:PublishTrimmed=false

if errorlevel 1 (
  echo BUILD FAILED
  pause
  exit /b 1
)

set OUT_DIR=bin\Release\net8.0-windows\win-x64\publish
set EXE_NAME=DHub.exe

if not exist "%OUT_DIR%\%EXE_NAME%" (
  echo EXE NOT FOUND
  echo %OUT_DIR%\%EXE_NAME%
  pause
  exit /b 1
)

if exist release rmdir /s /q release
mkdir release

copy "%OUT_DIR%\%EXE_NAME%" "release\%EXE_NAME%" > nul

if errorlevel 1 (
  echo COPY FAILED
  pause
  exit /b 1
)

echo DONE
echo release\DHub.exe

pause
