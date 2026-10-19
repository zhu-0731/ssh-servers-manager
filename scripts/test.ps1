param([switch]$Integration)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    if ($Integration) {
        $fixturePython = Join-Path $repo 'runtime/test-venv/Scripts/python.exe'
        if (-not (Test-Path -LiteralPath $fixturePython)) { throw 'Create runtime/test-venv and install tests/fixtures/requirements.txt first. See docs/testing.md.' }
        dotnet run --project tests/SshManager.Tests -- --integration $fixturePython (Join-Path $repo 'tests/fixtures/ssh_server.py')
    } else {
        dotnet run --project tests/SshManager.Tests
    }
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
} finally { Pop-Location }
