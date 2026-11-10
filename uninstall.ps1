# Remove TinyHand for Windows and its saved settings.
#
#   irm https://raw.githubusercontent.com/CredibleThought/tinyhand-windows/main/uninstall.ps1 | iex
#
# Settings > Apps > TinyHand > Uninstall runs this too.
#
# Everything is inside one script block, so a partly downloaded script never runs.

& {
    $ErrorActionPreference = 'Continue'

    $dir = Join-Path $env:LOCALAPPDATA 'Programs\TinyHand'
    $exe = Join-Path $dir 'TinyHand.exe'
    $startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'TinyHand'

    if (Test-Path $exe) {
        Start-Process $exe -ArgumentList '--quit' -Wait -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 1500
    }
    Get-Process TinyHand -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500

    # TinyHand hides the mouse pointer while it is on. If it was stopped
    # without a chance to clean up, bring the normal pointers back.
    Add-Type -Namespace TinyHand -Name Cursors -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action, uint param, System.IntPtr pv, uint ini);
'@
    [TinyHand.Cursors]::SystemParametersInfo(0x57, 0, [IntPtr]::Zero, 0) | Out-Null

    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'TinyHand' -ErrorAction SilentlyContinue
    Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\TinyHand' -Recurse -ErrorAction SilentlyContinue
    Remove-Item $startMenu -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $env:APPDATA 'TinyHand') -Recurse -Force -ErrorAction SilentlyContinue

    Write-Host 'TinyHand is removed.' -ForegroundColor Cyan
}
