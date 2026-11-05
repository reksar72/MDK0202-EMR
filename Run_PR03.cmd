@echo off
cd /d "%~dp0"
dotnet run --project src/AIS.MappingCli -- data/source-patient.json verification
pause
