@echo off
setlocal EnableExtensions EnableDelayedExpansion

echo ========================================
echo DHub GitHub Zip Release Build
echo ========================================

set ROOT_DIR=%~dp0
set PROJECT_DIR=%ROOT_DIR%src\ShortcutList
set PROJECT_FILE=%PROJECT_DIR%\ShortcutList.csproj
set UPDATER_DIR=%ROOT_DIR%src\Updater
set RELEASE_DIR=%ROOT_DIR%release
set EXE_NAME=DHub.exe
set UPDATER_EXE_NAME=DHubUpdater.exe

set REPO_OWNER=Chairman-bits
set REPO_NAME=DHub
set BRANCH_NAME=main

cd /d "%ROOT_DIR%"

set /p VERSION=Enter version (ex: 1.0.1): 

if "%VERSION%"=="" (
  echo [ERROR] version is required.
  pause
  exit /b 1
)

set APP_ZIP_URL=https://raw.githubusercontent.com/%REPO_OWNER%/%REPO_NAME%/%BRANCH_NAME%/DHub.zip
set UPDATER_ZIP_URL=https://raw.githubusercontent.com/%REPO_OWNER%/%REPO_NAME%/%BRANCH_NAME%/DHubUpdater.zip
set NOTES_URL=https://raw.githubusercontent.com/%REPO_OWNER%/%REPO_NAME%/%BRANCH_NAME%/release-notes.json

echo.
echo [INFO] version=%VERSION%
echo [INFO] app zip=%APP_ZIP_URL%
echo [INFO] updater zip=%UPDATER_ZIP_URL%
echo.

echo [1/6] update csproj version...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$p='%PROJECT_FILE%';" ^
  "$version='%VERSION%';" ^
  "$text=Get-Content $p -Raw;" ^
  "function Set-XmlTag($name,$value) {" ^
  " if ($script:text -match ('<' + $name + '>.*?</' + $name + '>')) { $script:text = [regex]::Replace($script:text, '<' + $name + '>.*?</' + $name + '>', '<' + $name + '>' + $value + '</' + $name + '>', 1); }" ^
  " else { $script:text = $script:text -replace '</PropertyGroup>', '    <' + $name + '>' + $value + '</' + $name + '>' + [Environment]::NewLine + '  </PropertyGroup>'; }" ^
  "}" ^
  "Set-XmlTag 'Version' $version;" ^
  "Set-XmlTag 'AssemblyVersion' ($version + '.0');" ^
  "Set-XmlTag 'FileVersion' ($version + '.0');" ^
  "Set-Content -Path $p -Value $text -Encoding UTF8;"

if errorlevel 1 (
  echo [ERROR] version update failed.
  pause
  exit /b 1
)

echo [2/6] clean...
if exist "%RELEASE_DIR%" rmdir /s /q "%RELEASE_DIR%"
mkdir "%RELEASE_DIR%"

echo [3/6] publish DHub...
cd /d "%PROJECT_DIR%"

dotnet publish -c Release -r win-x64 --self-contained true ^
 /p:PublishSingleFile=true ^
 /p:IncludeNativeLibrariesForSelfExtract=true ^
 /p:EnableCompressionInSingleFile=true ^
 /p:PublishTrimmed=false

if errorlevel 1 (
  echo [ERROR] DHub publish failed.
  pause
  exit /b 1
)

echo [4/6] publish updater...
cd /d "%UPDATER_DIR%"

dotnet publish -c Release -r win-x64 --self-contained true ^
 /p:PublishSingleFile=true ^
 /p:PublishTrimmed=false

if errorlevel 1 (
  echo [ERROR] updater publish failed.
  pause
  exit /b 1
)

echo [5/6] create compressed zip files...
set APP_PUBLISH=%PROJECT_DIR%\bin\Release\net8.0-windows\win-x64\publish
set UPDATER_PUBLISH=%UPDATER_DIR%\bin\Release\net8.0\win-x64\publish

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$release='%RELEASE_DIR%';" ^
  "$app='%APP_PUBLISH%\%EXE_NAME%';" ^
  "$updater='%UPDATER_PUBLISH%\%UPDATER_EXE_NAME%';" ^
  "Compress-Archive -Path $app -DestinationPath (Join-Path $release 'DHub.zip') -Force;" ^
  "Compress-Archive -Path $updater -DestinationPath (Join-Path $release 'DHubUpdater.zip') -Force;"

if errorlevel 1 (
  echo [ERROR] zip creation failed.
  pause
  exit /b 1
)

echo [6/6] generate version.json...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$version='%VERSION%';" ^
  "$app='%APP_ZIP_URL%';" ^
  "$updater='%UPDATER_ZIP_URL%';" ^
  "$notes='%NOTES_URL%';" ^
  "$release='%RELEASE_DIR%';" ^
  "$obj=[ordered]@{ version=$version; downloadUrl=$app; updaterUrl=$updater; releaseNotes=$notes };" ^
  "$json=$obj | ConvertTo-Json -Depth 5;" ^
  "Set-Content -Path (Join-Path $release 'version.json') -Value $json -Encoding UTF8;" ^
  "$notesObj=[ordered]@{ version=$version; notes=@('DHub v' + $version) };" ^
  "$notesJson=$notesObj | ConvertTo-Json -Depth 5;" ^
  "Set-Content -Path (Join-Path $release 'release-notes.json') -Value $notesJson -Encoding UTF8;"

if errorlevel 1 (
  echo [ERROR] metadata generation failed.
  pause
  exit /b 1
)

echo.
echo ========================================
echo DONE
echo ========================================
echo Put these files on main branch root:
echo   release\DHub.zip
echo   release\DHubUpdater.zip
echo   release\version.json
echo   release\release-notes.json
echo.
pause
