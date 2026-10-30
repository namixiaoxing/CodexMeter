$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $projectRoot 'dist\CodexMeter.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Build CodexMeter first.' }
$setup = Start-Process -FilePath $executable -ArgumentList '--enable-autofollow' -WindowStyle Hidden -PassThru
if (-not $setup.WaitForExit(20000)) { throw 'Startup registration timed out.' }
if ($setup.ExitCode -ne 0) { throw (Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'CodexMeter\startup-error.txt') -Raw) }
Write-Output 'Enabled and started the independent current-user Windows startup task. Codex itself is not auto-started.'
