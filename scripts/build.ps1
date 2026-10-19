param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    dotnet build SshServersManager.sln -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    if ($Publish) {
        dotnet publish src/SshManager.App/SshManager.App.csproj -c Release --no-restore -o artifacts/windows-x64
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    }
} finally { Pop-Location }
