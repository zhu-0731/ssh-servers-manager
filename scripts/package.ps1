param([string]$RuntimeVersion = '8.0.32')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    if (git status --porcelain) { throw 'Commit or stash project changes before packaging a release snapshot.' }
    $revision = (git rev-parse HEAD).Trim()
    [xml]$project = Get-Content src/SshManager.App/SshManager.App.csproj -Raw
    $version = [string]$project.Project.PropertyGroup.Version
    $work = Join-Path $repo ('runtime/package-' + $version + '-' + [guid]::NewGuid().ToString('N'))
    $source = Join-Path $work 'source'
    $publish = Join-Path $work 'publish'
    $stage = Join-Path $work ('SSH-Servers-Manager-v' + $version + '-win-x64')
    $packages = Join-Path $repo 'runtime/packages'
    $release = Join-Path $repo ('artifacts/releases/v' + $version)
    New-Item -ItemType Directory -Path $work,$stage,$release -Force | Out-Null
    $archive = Join-Path $work 'source.zip'
    git archive --format=zip HEAD -o $archive
    if ($LASTEXITCODE -ne 0) { throw 'Source archive failed.' }
    Expand-Archive -LiteralPath $archive -DestinationPath $source
    # Publishing the committed snapshot keeps self-contained restore changes out of the real lock files.
    dotnet publish (Join-Path $source 'src/SshManager.App/SshManager.App.csproj') -c Release --self-contained true -p:RuntimeFrameworkVersion=$RuntimeVersion -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --packages $packages -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }
    Copy-Item -LiteralPath (Join-Path $publish 'SshServersManager.exe') -Destination $stage
    $unexpected = @(Get-ChildItem -LiteralPath $publish -File | Where-Object { $_.Extension -notin '.exe','.pdb','.xml' })
    if ($unexpected.Count -gt 0) { throw ('Unexpected non-bundled runtime files: ' + ($unexpected.Name -join ', ')) }
    Copy-Item -LiteralPath (Join-Path $source 'docs/user-guide.md') -Destination (Join-Path $stage 'USER-GUIDE.zh-CN.md')
    Copy-Item -LiteralPath (Join-Path $source 'CHANGELOG.md') -Destination $stage
    $readme = @"
SSH Servers Manager v$version - Windows x64 portable preview

Unzip the entire folder and run SshServersManager.exe.
The .NET $RuntimeVersion Desktop Runtime is bundled; no separate .NET installation is required.
Administrator permissions are not required. Windows x64 only.

See USER-GUIDE.zh-CN.md for Chinese instructions.
User configuration is stored in %LOCALAPPDATA%\SshServersManager.
Passwords and private-key passphrases use Windows Credential Manager.
No user connection data, credentials, private keys or smoke-test files are included.

Source revision: $revision
This is an unsigned preview build. See the release notes for tested scope and limitations.
"@
    [IO.File]::WriteAllText((Join-Path $stage 'README.txt'), $readme, [Text.UTF8Encoding]::new($false))
    $licenses = Join-Path $stage 'third-party-licenses'
    New-Item -ItemType Directory -Path $licenses | Out-Null
    foreach ($dependency in @('bouncycastle.cryptography/2.7.0','microsoft.extensions.logging.abstractions/8.0.3','microsoft.extensions.dependencyinjection.abstractions/8.0.2',"microsoft.netcore.app.runtime.win-x64/$RuntimeVersion","microsoft.windowsdesktop.app.runtime.win-x64/$RuntimeVersion")) {
        $directory = Join-Path $packages $dependency
        $notices = @(Get-ChildItem -LiteralPath $directory -File | Where-Object { $_.Name -match '(?i)license|third.party.notices' })
        if ($notices.Count -eq 0) { throw "Missing dependency license notices: $dependency" }
        foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licenses ($dependency.Replace('/','-') + '-' + $notice.Name)) }
    }
    Invoke-WebRequest -UseBasicParsing 'https://raw.githubusercontent.com/sshnet/SSH.NET/2026.0.0/LICENSE' -OutFile (Join-Path $licenses 'SSH.NET-2026.0.0-LICENSE.txt')
    $manifest = @{ version = $version; commit = $revision; runtime = $RuntimeVersion; platform = 'win-x64'; selfContained = $true; builtAtUtc = [DateTime]::UtcNow.ToString('o') }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'build-info.json') -Encoding UTF8
    # Only allowlisted distributable files enter the ZIP; UI tests run in a separate publish directory.
    $smoke = Start-Process -FilePath (Join-Path $publish 'SshServersManager.exe') -ArgumentList '--smoke-test' -WindowStyle Hidden -PassThru
    if (-not $smoke.WaitForExit(30000)) { $smoke.Kill(); throw 'Packaged executable UI smoke test timed out.' }
    if ($smoke.ExitCode -ne 0) { throw ('Packaged executable UI smoke failed. See ' + (Join-Path $publish 'smoke-error.txt')) }
    $zip = Join-Path $release ('SSH-Servers-Manager-v' + $version + '-win-x64.zip')
    Compress-Archive -LiteralPath $stage -DestinationPath $zip -CompressionLevel Optimal -Force
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(($zip + '.sha256'), ($hash + '  ' + [IO.Path]::GetFileName($zip) + "`n"), [Text.Encoding]::ASCII)
    Write-Output "ZIP: $zip"
    Write-Output "SHA256: $hash"
    Write-Output "Source commit: $revision"
} finally { Pop-Location }
