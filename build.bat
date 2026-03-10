@echo off
setlocal

rem Build pipeline: icon conversion, restore, publish, installer packaging.
set "ROOT=%~dp0"
set "APPDATA=%ROOT%AppData\Roaming"
set "DOTNET_CLI_HOME=%ROOT%.dotnet"
set "NUGET_PACKAGES=%ROOT%.nuget\packages"
set "NUGET_HTTP_CACHE_PATH=%ROOT%.nuget\http-cache"
set "NUGET_CONFIG=%ROOT%NuGet.Config"
set "PNG_ICON=%ROOT%Spaste\Resources\app.png"
set "ICO_ICON=%ROOT%Spaste\Resources\app.ico"
set "PUBLISH_DIR=%ROOT%publish"
set "ISCC_EXE=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

if not exist "%APPDATA%\NuGet" mkdir "%APPDATA%\NuGet"

echo [1/4] Convert PNG icon to ICO
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%scripts\Convert-PngToIco.ps1" -PngPath "%PNG_ICON%" -IcoPath "%ICO_ICON%"
if errorlevel 1 goto :error

echo [2/4] Restore NuGet packages
dotnet restore "%ROOT%Spaste.sln" -r win-x64 --configfile "%NUGET_CONFIG%"
if errorlevel 1 goto :error

echo [3/4] Publish release build
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
dotnet publish "%ROOT%Spaste\Spaste.csproj" -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=false -o "%PUBLISH_DIR%"
if errorlevel 1 goto :error

echo [4/4] Build Inno Setup installer
if not exist "%ISCC_EXE%" set "ISCC_EXE=C:\Program Files\Inno Setup 6\ISCC.exe"
if not exist "%ISCC_EXE%" set "ISCC_EXE=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not exist "%ISCC_EXE%" (
    echo [ERROR] ISCC.exe was not found. Install Inno Setup 6 and run build.bat again.
    exit /b 1
)

"%ISCC_EXE%" "%ROOT%Installer\Spaste.iss"
if errorlevel 1 goto :error

echo [DONE] Publish output and installer were generated successfully.
exit /b 0

:error
echo [FAILED] Build or packaging step failed.
exit /b 1
