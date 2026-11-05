@echo off
cd /d "%~dp0"
dotnet run --project src/AIS.Gateway -- --demo
pause
