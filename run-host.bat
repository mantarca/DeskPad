@echo off
chcp 65001 > nul
cd /d "%~dp0host-pc\src\DeskPad.Host"
dotnet run --configuration Release
pause
