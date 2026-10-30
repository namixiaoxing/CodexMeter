param([string]$DotnetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src\CodexMeter.csproj'
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
$outputRoot = Join-Path $projectRoot 'artifacts'
$stage = Join-Path $outputRoot ('package-' + $version + '-' + [guid]::NewGuid().ToString('N'))
$publish = Join-Path $stage 'build'
$portable = Join-Path $stage ('CodexMeter-v' + $version + '-win-x64')
$source = Join-Path $stage ('CodexMeter-v' + $version + '-source')
New-Item -ItemType Directory -Path $publish,(Join-Path $portable 'dist'),(Join-Path $portable 'scripts'),$source -Force | Out-Null

& $DotnetPath run --project (Join-Path $projectRoot 'tests\CodexMeter.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Checks failed; nothing was packaged.' }
& $DotnetPath publish $project -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true ('-p:InformationalVersion=' + $version) -p:IncludeSourceRevisionInInformationalVersion=false ('-p:PathMap=' + $projectRoot + '=/_/CodexMeter') -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed; nothing was packaged.' }

# Copy a positive allowlist. Never copy an existing dist/, settings directory, or diagnostics.
Copy-Item -LiteralPath (Join-Path $publish 'CodexMeter.exe') -Destination (Join-Path $portable 'dist')
foreach ($name in @('README.md','THIRD-PARTY-NOTICES.txt','CHANGELOG.md','TROUBLESHOOTING.md','RELEASE-NOTES.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $portable
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $source
}
Copy-Item -LiteralPath (Join-Path $projectRoot '.gitignore') -Destination $source
foreach ($root in @($portable,$source)) {
    $screenshots = Join-Path $root 'assets\screenshots'
    New-Item -ItemType Directory -Path $screenshots -Force | Out-Null
    foreach ($name in @('panel-miku.png','overlay.png')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot ('assets\screenshots\' + $name)) -Destination $screenshots
    }
}
foreach ($name in @('Enable-AutoFollow.ps1','Disable-AutoFollow.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $portable 'scripts')
}
foreach ($directory in @('src','tests')) {
    $target = Join-Path $source $directory
    New-Item -ItemType Directory -Path $target | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $projectRoot $directory) -File |
        Where-Object { $_.Extension -in @('.cs','.csproj','.manifest') } |
        Copy-Item -Destination $target
}
New-Item -ItemType Directory -Path (Join-Path $source 'scripts') | Out-Null
foreach ($name in @('Build.ps1','Package.ps1','Enable-AutoFollow.ps1','Disable-AutoFollow.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $source 'scripts')
}

# Refuse common credential formats and accidental absolute local profile paths.
$sensitive = '(?i)(?:\bsk-[A-Za-z0-9_-]{24,}|\bgh[pousr]_[A-Za-z0-9]{24,}|\bgithub_pat_[A-Za-z0-9_]{24,}|-----BEGIN [A-Z ]*PRIVATE KEY-----|[A-Z]:[\\/]Users[\\/][^\\/\s"<>]+)'
foreach ($root in @($portable,$source)) {
    foreach ($file in Get-ChildItem -LiteralPath $root -File -Recurse) {
        $bytes = [IO.File]::ReadAllBytes($file.FullName)
        $texts = @([Text.Encoding]::UTF8.GetString($bytes),[Text.Encoding]::Unicode.GetString($bytes))
        if ($bytes.Length -gt 1) { $texts += [Text.Encoding]::Unicode.GetString($bytes,1,$bytes.Length-1) }
        foreach ($text in $texts) {
            if ($text -match $sensitive -or $text.Contains($projectRoot) -or $text.Contains($env:USERPROFILE)) {
                throw ('Potential private data in ' + $file.Name + '; nothing was archived.')
            }
        }
    }
}

$portableZip = Join-Path $outputRoot ('CodexMeter-v' + $version + '-win-x64.zip')
$sourceZip = Join-Path $outputRoot ('CodexMeter-v' + $version + '-source.zip')
Compress-Archive -LiteralPath $portable -DestinationPath $portableZip -Force
Compress-Archive -LiteralPath $source -DestinationPath $sourceZip -Force
$hashes = foreach ($archive in @($portableZip,$sourceZip)) {
    (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($archive)
}
$hashes | Set-Content -LiteralPath (Join-Path $outputRoot 'SHA256SUMS.txt') -Encoding utf8
Write-Output ('Portable: ' + $portableZip)
Write-Output ('Source:   ' + $sourceZip)
Write-Output ('Staging:  ' + $stage)
