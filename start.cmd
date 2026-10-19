@echo off
cd /d "%~dp0"
if exist "artifacts\windows-x64\SshServersManager.exe" (
    start "" "artifacts\windows-x64\SshServersManager.exe"
) else (
    dotnet run --project src\SshManager.App
    if errorlevel 1 pause
)
