@echo off
chcp 65001 >nul
setlocal
set "ROOT=%~dp0.."
cd /d "%ROOT%"
set "LOG=%ROOT%\Tools\build.log"
echo [%date% %time%] dotnet build > "%LOG%"
dotnet build KLauncher.csproj -c Debug -nologo >> "%LOG%" 2>&1
set CODE=%ERRORLEVEL%
echo EXIT=%CODE%>> "%LOG%"
if not "%CODE%"=="0" exit /b %CODE%
taskkill /im KLauncher.exe /f >nul 2>&1
timeout /t 2 /nobreak >nul
start "" "%ROOT%\bin\Debug\net10.0-windows\KLauncher.exe"
echo STARTED>> "%LOG%"
timeout /t 30 /nobreak >nul
call "%~dp0measure-ram.cmd" "derlemeden 30 sn sonra"
