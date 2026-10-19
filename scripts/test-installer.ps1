param([string]$SetupPath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $repo ('runtime/installer-test-' + [guid]::NewGuid().ToString('N'))
$install = Join-Path $testRoot 'installed'
$groupName = 'SSH Servers Manager Installer Test ' + [guid]::NewGuid().ToString('N').Substring(0,8)
$shortcuts = Join-Path ([Environment]::GetFolderPath('Programs')) $groupName
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'SSH Servers Manager.lnk'
$testDesktop = -not (Test-Path -LiteralPath $desktopShortcut)
$registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SSHServersManager.zhu-0731_is1'
if (Test-Path -LiteralPath $registry) { throw 'An installed copy already exists; use a clean Windows test account to avoid changing it.' }
if (-not $SetupPath) {
    [xml]$project = Get-Content (Join-Path $repo 'src/SshManager.App/SshManager.App.csproj') -Raw
    $version = [string]$project.Project.PropertyGroup.Version
    $SetupPath = Join-Path $repo "artifacts/releases/v$version/SSH-Servers-Manager-v$version-win-x64-Setup.exe"
}
$SetupPath = [IO.Path]::GetFullPath($SetupPath)
New-Item -ItemType Directory -Path $testRoot | Out-Null
function Run-Checked([string]$file, [string[]]$arguments) {
    $process = Start-Process -FilePath $file -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(30000)) { throw "Process did not finish in 30 seconds: $file" }
    if ($process.ExitCode -ne 0) { throw "Process failed with exit code $($process.ExitCode): $file" }
}
function Uninstall-TestCopy {
    $uninstaller = Join-Path $install 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller) {
        Run-Checked $uninstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $testRoot 'uninstall.log') + '"'))
    }
}
try {
    $tasks = if ($testDesktop) { '/TASKS=desktopicon' } else { '/TASKS=""' }
    $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOCLOSEAPPLICATIONS','/LANG=chinesesimp',$tasks,('/DIR="' + $install + '"'),('/GROUP="' + $groupName + '"'),('/LOG="' + (Join-Path $testRoot 'install.log') + '"'))
    Run-Checked $SetupPath $arguments
    $exe = Join-Path $install 'SshServersManager.exe'
    if (-not (Test-Path -LiteralPath $exe) -or -not (Test-Path -LiteralPath $registry)) { throw 'Installed executable or uninstall registration missing.' }
    if (-not (Test-Path -LiteralPath (Join-Path $shortcuts 'SSH Servers Manager.lnk'))) { throw 'Start menu shortcut missing.' }
    $registered = (Get-ItemProperty -LiteralPath $registry).InstallLocation
    if ($registered.TrimEnd('\') -ne $install.TrimEnd('\')) { throw 'Unexpected install registration; refusing further mutation.' }
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut((Join-Path $shortcuts 'SSH Servers Manager.lnk'))
    if ($link.TargetPath -ne $exe) { throw 'Start menu shortcut target is incorrect.' }
    if ($testDesktop) {
        if (-not (Test-Path -LiteralPath $desktopShortcut) -or $shell.CreateShortcut($desktopShortcut).TargetPath -ne $exe) { throw 'Desktop shortcut missing or incorrect.' }
    }
    Run-Checked $exe @('--smoke-test')
    if (-not (Test-Path -LiteralPath (Join-Path $install 'ui-smoke.png'))) { throw 'Installed application did not complete UI regression.' }
    $originalHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    Run-Checked $SetupPath $arguments
    if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -ne $originalHash) { throw 'Repair installation changed application bytes.' }
    $notes = Join-Path $install 'user-preserved.txt'
    Set-Content -LiteralPath $notes -Value 'Uninstaller must preserve files it did not install.' -Encoding ASCII
    Uninstall-TestCopy
    if ((Test-Path -LiteralPath $exe) -or (Test-Path -LiteralPath $registry) -or (Test-Path -LiteralPath (Join-Path $shortcuts 'SSH Servers Manager.lnk'))) { throw 'Uninstall did not remove owned files, shortcut or registration.' }
    if (-not (Test-Path -LiteralPath $notes)) { throw 'Uninstaller removed an unrelated user file.' }
    if ($testDesktop -and (Test-Path -LiteralPath $desktopShortcut)) { throw 'Test desktop shortcut was not removed.' }
    Write-Output 'PASS: install, Start menu target, installed UI, repair, uninstall, unrelated-file preservation.'
    Write-Output "Desktop shortcut tested: $testDesktop"
    Write-Output "Test logs retained in: $testRoot"
} finally {
    # Only invoke the uninstaller belonging to this exact workspace test target.
    $expectedPrefix = [IO.Path]::GetFullPath((Join-Path $repo 'runtime')) + [IO.Path]::DirectorySeparatorChar
    if (-not [IO.Path]::GetFullPath($install).StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Test target escaped runtime directory.' }
    if (Test-Path -LiteralPath $registry) {
        $actual = (Get-ItemProperty -LiteralPath $registry).InstallLocation
        if ($actual.TrimEnd('\') -eq $install.TrimEnd('\')) { Uninstall-TestCopy }
    }
}
