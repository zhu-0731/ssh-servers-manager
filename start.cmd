@echo off
cd /d "%~dp0"
if exist "artifacts\windows-x64-v0.1.3\SshServersManager.exe" (
    start "" "artifacts\windows-x64-v0.1.3\SshServersManager.exe"
) else if exist "artifacts\windows-x64-v0.1.2\SshServersManager.exe" (
    start "" "artifacts\windows-x64-v0.1.2\SshServersManager.exe"
) else if exist "artifacts\windows-x64-v0.1.1\SshServersManager.exe" (
    start "" "artifacts\windows-x64-v0.1.1\SshServersManager.exe"
) else if exist "artifacts\windows-x64\SshServersManager.exe" (
    start "" "artifacts\windows-x64\SshServersManager.exe"
) else (
    dotnet run --project src\SshManager.App
    if errorlevel 1 pause
)
