# Install or update TinyHand for Windows from the latest GitHub release.
#
#   irm https://raw.githubusercontent.com/CredibleThought/tinyhand-windows/main/install.ps1 | iex
#
# Set $env:TINYHAND_EXE to a local TinyHand.exe to install that instead of downloading.
#
# Everything is inside one script block, so a partly downloaded script never runs.

& {
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'

    $repo = 'https://github.com/CredibleThought/tinyhand-windows'
    $raw = 'https://raw.githubusercontent.com/CredibleThought/tinyhand-windows/main'
    $dir = Join-Path $env:LOCALAPPDATA 'Programs\TinyHand'
    $exe = Join-Path $dir 'TinyHand.exe'
    $startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'TinyHand'
    $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\TinyHand'
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

    function Say($text) { Write-Host $text -ForegroundColor Cyan }

    # Pick the build for this PC. On ARM, an x64 PowerShell still reports the real CPU here.
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    $build = if ($arch -eq 'Arm64') { 'arm64' } else { 'x64' }
    if ($arch -ne 'Arm64' -and $arch -ne 'X64') { throw "TinyHand needs 64-bit Windows (this PC is $arch)." }

    # Close a running copy so its file can be replaced.
    if (Test-Path $exe) {
        Say 'Closing TinyHand...'
        Start-Process $exe -ArgumentList '--quit' -Wait -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 1500
    }
    Get-Process TinyHand -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500

    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    if ($env:TINYHAND_EXE) {
        Copy-Item $env:TINYHAND_EXE $exe -Force
    } else {
        Say "Downloading TinyHand ($build)..."
        Invoke-WebRequest "$repo/releases/latest/download/TinyHand-$build.exe" -OutFile $exe -UseBasicParsing
        Invoke-WebRequest "$raw/uninstall.ps1" -OutFile (Join-Path $dir 'uninstall.ps1') -UseBasicParsing
    }
    Unblock-File $exe
    $uninstaller = Join-Path $dir 'uninstall.ps1'
    if ($PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot 'uninstall.ps1'))) {
        Copy-Item (Join-Path $PSScriptRoot 'uninstall.ps1') $uninstaller
    }

    Say 'Adding shortcuts...'
    New-Item -ItemType Directory -Force -Path $startMenu | Out-Null
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut((Join-Path $startMenu 'TinyHand.lnk'))
    $link.TargetPath = $exe
    $link.Description = 'Turn the TinyHand pointer on'
    $link.Save()
    $link = $shell.CreateShortcut((Join-Path $startMenu 'TinyHand Settings.lnk'))
    $link.TargetPath = $exe
    $link.Arguments = '--settings'
    $link.Description = 'TinyHand settings'
    $link.Save()

    # Start when you sign in, and show up in Settings > Apps so it can be removed there.
    New-ItemProperty -Path $runKey -Name 'TinyHand' -Value "`"$exe`"" -PropertyType String -Force | Out-Null
    New-Item -Path $uninstallKey -Force | Out-Null
    $version = (Get-Item $exe).VersionInfo.ProductVersion -replace '\+.*$', ''
    $values = @{
        DisplayName = 'TinyHand'
        DisplayVersion = $version
        Publisher = 'Steve Fernandes'
        DisplayIcon = $exe
        InstallLocation = $dir
        URLInfoAbout = $repo
        UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstaller`""
    }
    foreach ($name in $values.Keys) {
        New-ItemProperty -Path $uninstallKey -Name $name -Value $values[$name] -PropertyType String -Force | Out-Null
    }
    New-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null

    Start-Process $exe
    Say 'TinyHand is installed and running.'
    Write-Host 'Press Ctrl+Alt+H, or click the hand in the notification area, to turn the pointer on.'
    Write-Host 'If you cannot see the hand icon, look under the ^ arrow next to the clock.'
}
