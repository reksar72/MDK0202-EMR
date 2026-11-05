@echo off
cd /d "%~dp0"
dotnet run --project tests/AIS.Tests -- verification
pause
