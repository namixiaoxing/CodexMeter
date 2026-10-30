param([string]$DotnetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& $DotnetPath run --project (Join-Path $projectRoot 'tests\CodexMeter.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Parser checks failed.' }
& $DotnetPath publish (Join-Path $projectRoot 'src\CodexMeter.csproj') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o (Join-Path $projectRoot 'dist') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md'),(Join-Path $projectRoot 'THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $projectRoot 'dist') -Force
