param([string]$IsccPath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    [xml]$project = Get-Content src/SshManager.App/SshManager.App.csproj -Raw
    $version = [string]$project.Project.PropertyGroup.Version
    if (-not $IsccPath) {
        $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        $candidates = @(
            (Join-Path $repo 'runtime/tools/inno-6.7.3/ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe')
        )
        if ($command) { $candidates += $command.Source }
        $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
    if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) { throw 'Inno Setup 6 compiler not found. Install Inno Setup or pass -IsccPath. See installer/README.md.' }
    $release = Join-Path $repo ('artifacts/releases/v' + $version)
    $zip = Join-Path $release ('SSH-Servers-Manager-v' + $version + '-win-x64.zip')
    if (-not (Test-Path -LiteralPath $zip)) { throw 'Build the portable ZIP with scripts/package.ps1 first.' }
    $expected = ((Get-Content -LiteralPath ($zip + '.sha256') -Raw).Trim() -split '\s+')[0]
    if ($expected -ne (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Portable ZIP checksum mismatch.' }
    $work = Join-Path $repo ('runtime/installer-' + $version + '-' + [guid]::NewGuid().ToString('N'))
    Expand-Archive -LiteralPath $zip -DestinationPath $work
    $payload = Join-Path $work ('SSH-Servers-Manager-v' + $version + '-win-x64')
    $manifest = Get-Content -LiteralPath (Join-Path $payload 'build-info.json') -Raw | ConvertFrom-Json
    if (-not $manifest.selfContained -or $manifest.version -ne $version) { throw 'Expected a matching self-contained application package.' }
    & $IsccPath "/DAppVersion=$version" "/DPayloadDir=$payload" "/DOutputDir=$release" "/DProjectDir=$repo" (Join-Path $repo 'installer/setup.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
    $setup = Join-Path $release ('SSH-Servers-Manager-v' + $version + '-win-x64-Setup.exe')
    $hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(($setup + '.sha256'), ($hash + '  ' + [IO.Path]::GetFileName($setup) + "`n"), [Text.Encoding]::ASCII)
    Write-Output "Setup: $setup"
    Write-Output "SHA256: $hash"
    Write-Output "Application source commit: $($manifest.commit)"
} finally { Pop-Location }
