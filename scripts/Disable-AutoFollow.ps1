$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $projectRoot 'dist\CodexMeter.exe'
$setup = Start-Process -FilePath $executable -ArgumentList '--disable-autofollow' -WindowStyle Hidden -PassThru
if (-not $setup.WaitForExit(20000)) { throw 'Startup removal timed out.' }
if ($setup.ExitCode -ne 0) { throw (Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'CodexMeter\startup-error.txt') -Raw) }
Write-Output 'Removed the current-user startup task and legacy sign-in entry. Exit the running listener from the tray to stop it now.'
