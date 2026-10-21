@echo off
rem Builds the Kofre server for Linux as a zip, in artifacts\installer: kofre-server-<version>-linux-x64.zip
rem Needs the .NET 10 SDK. An installer may come later.
setlocal
cd /d "%~dp0.."

set "PROJECT=src\Kofre.Server\Kofre.Server.csproj"
set "RUNTIME=linux-x64"

rem The version comes from Directory.Build.props, the single source of truth.
set "VERSION="
for /f "usebackq delims=" %%v in (`dotnet msbuild "%PROJECT%" -nologo -getProperty:Version`) do set "VERSION=%%v"
if not defined VERSION (
  echo Could not read the version from Directory.Build.props.
  exit /b 1
)

echo Building the Kofre server %VERSION% zip...

rem Self-contained: the server needs no .NET runtime on the machine. The zip holds a kofre folder:
rem the server, its appsettings.json, the samples (systemd unit, Caddyfile) and the docs.
set "OUT=%CD%\artifacts\installer"
set "PACKAGE=%OUT%\zip\kofre"
set "ZIP=%OUT%\kofre-server-%VERSION%-%RUNTIME%.zip"
if exist "%OUT%\zip" rmdir /s /q "%OUT%\zip"
rem One file, compressed: the runtime and ASP.NET Core packed into Kofre.Server, beside appsettings.json.
dotnet publish "%PROJECT%" -c Release -r %RUNTIME% --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o "%PACKAGE%" || exit /b 1
xcopy "samples" "%PACKAGE%\samples\" /e /i /q >nul || exit /b 1
copy /y README.md "%PACKAGE%\" >nul || exit /b 1
copy /y CHANGELOG.md "%PACKAGE%\" >nul || exit /b 1
copy /y LICENSE "%PACKAGE%\" >nul || exit /b 1

if exist "%ZIP%" del "%ZIP%"
rem tar (in Windows 10 and later) writes / in paths, as unzip on Linux expects; Compress-Archive may write \.
"%SystemRoot%\System32\tar.exe" -a -c -f "%ZIP%" -C "%OUT%\zip" kofre || exit /b 1

echo.
echo Zip: artifacts\installer\kofre-server-%VERSION%-%RUNTIME%.zip
