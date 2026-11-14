# Free DungeonMapPilot test-edition ZIP used by the RaidPilot launcher only.
# Public paid source is built separately by PilotSuite CurseForge publisher.
param(
    [ValidateSet('raidpilot-test')][string]$Edition='raidpilot-test',
    [string]$Source=(Join-Path $PSScriptRoot 'source/dungeonmappilot'),
    [string]$OutputRoot=(Join-Path $PSScriptRoot '../outputs')
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sourcePath=(Resolve-Path -LiteralPath $Source).Path
$tocPath=Join-Path $sourcePath 'DungeonMapPilot.toc'
$raw=Get-Content -LiteralPath $tocPath -Raw -Encoding UTF8
$match=[regex]::Match($raw,'(?m)^## Version:\s*(\d+\.\d+\.\d+)\s*$')
if (!$match.Success) {throw 'Missing numeric free launcher edition version'}
$version=$match.Groups[1].Value
$access=Get-Content -LiteralPath (Join-Path $sourcePath 'Access.lua') -Raw -Encoding UTF8
if ($access -notmatch 'A\.distribution = "raidpilot-test"' -or
    $access -match 'A\.distribution = "subscriber"') {
    throw 'Launcher package MUST be the free RaidPilot testing edition'
}
$core=Get-Content -LiteralPath (Join-Path $sourcePath 'Core.lua') -Raw -Encoding UTF8
if (!$core.Contains('version = "' + $version + '"')) {throw 'TOC/Core versions disagree'}
$lines=@($raw -split "\r?\n" | ForEach-Object {$_.Trim()} | Where-Object {
    $_ -match '\.lua$'
})
if ($lines.Count -lt 30 -or $lines.Count -gt 100) {throw "Unexpected TOC module count: $($lines.Count)"}
$metadata=@('DungeonMapPilot.toc','README.md','RELEASE-NOTES.md','LICENSE.txt',
    'LICENSE-OpenMap.txt','GUIDES-STATUS.md','DATA-SOURCES.md')
$files=@($metadata+$lines|Sort-Object -Unique)
foreach($file in $files) {
    $relative=$file.Replace('\','/')
    if ($relative.StartsWith('/') -or $relative.Contains('../') -or
        $relative.Contains(':') -or $relative -notmatch '^[\w./-]+$') {
        throw "Unsafe addon file: $file"
    }
    $full=Join-Path $sourcePath $file
    if (!(Test-Path -LiteralPath $full -PathType Leaf)) {throw "Missing addon file: $file"}
    if ((Get-Item -LiteralPath $full).Length -gt 4194304) {throw "Unexpected oversized file: $file"}
}
$actual=@(Get-ChildItem -LiteralPath $sourcePath -Recurse -File | ForEach-Object {
    $_.FullName.Substring($sourcePath.TrimEnd('\').Length+1).Replace('\','/')
} | Sort-Object -Unique)
if(@(Compare-Object $files $actual).Count -ne 0) {throw 'Unexpected extra or missing bundle file'}
$label='raidpilot-test'
$destination=Join-Path $OutputRoot "DungeonMapPilot-v$version-$label"
if (Test-Path -LiteralPath $destination) {throw "Output already exists: $destination"}
$package=Join-Path $destination 'DungeonMapPilot'
New-Item -ItemType Directory -Path $package -Force | Out-Null
foreach($file in $files) {
    $src=Join-Path $sourcePath $file
    $dst=Join-Path $package $file
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dst)|Out-Null
    Copy-Item -LiteralPath $src -Destination $dst
    if ((Get-FileHash -LiteralPath $src -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash) {
        throw "Copy validation failed: $file"
    }
}
$zip=Join-Path $destination "DungeonMapPilot-v$version-$label.zip"
[System.IO.Compression.ZipFile]::CreateFromDirectory($package,$zip,
    [System.IO.Compression.CompressionLevel]::Optimal,$true)
$archive=[System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $entries=@($archive.Entries | ForEach-Object {$_.FullName.Replace('\','/')})
    $expected=@($files | ForEach-Object {"DungeonMapPilot/"+$_.Replace('\','/')})
    if (@(Compare-Object $entries $expected).Count -ne 0) {
        throw 'ZIP does not match the expected addon files'
    }
} finally {$archive.Dispose()}
[pscustomobject]@{
    Version=$version;Edition=$Edition;Files=$files.Count;Path=$zip
    SHA256=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    PublicRelease=$false
} | ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $destination 'verification.json') -Encoding UTF8
Write-Output "Verified free RaidPilot DungeonMapPilot $version, $($files.Count) files: $zip"
