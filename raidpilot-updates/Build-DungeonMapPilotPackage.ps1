param(
    [ValidateSet('raidpilot-test','public')][string]$Edition = 'raidpilot-test',
    [string]$Source = (Join-Path $PSScriptRoot 'source/dungeonmappilot'),
    [string]$OutputRoot = (Join-Path $PSScriptRoot '../outputs')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sourcePath = (Resolve-Path -LiteralPath $Source).Path
$toc = Get-Content -LiteralPath (Join-Path $sourcePath 'DungeonMapPilot.toc') -Raw
$version = [regex]::Match($toc, '(?m)^## Version: ([0-9]+\.[0-9]+\.[0-9]+)\s*$').Groups[1].Value
if (!$version) { throw 'Missing addon version' }
$core = Get-Content -LiteralPath (Join-Path $sourcePath 'Core.lua') -Raw
if (!$core.Contains('version = "' + $version + '"')) { throw 'TOC and Core versions differ' }
$luaFiles = @($toc -split '\r?\n' | Where-Object { $_ -match '^[A-Za-z]+\.lua$' })
$allowed = @('Core.lua','Access.lua','Locale.lua','ReferenceMaps.lua','Catalog.lua','Model.lua','RouteData.lua','Routes.lua','UI.lua','RouteUI.lua','Main.lua')
if (@(Compare-Object $luaFiles $allowed).Count -ne 0) { throw 'Review the changed TOC file list before packaging' }
$files = @('DungeonMapPilot.toc') + $luaFiles + @('README.md','LICENSE.txt','LICENSE-OpenMap.txt')
$label = if ($Edition -eq 'public') { 'public-candidate' } else { 'local' }
$destination = Join-Path $OutputRoot "DungeonMapPilot-v$version-$label"
if (Test-Path -LiteralPath $destination) { throw "Output already exists: $destination" }
$package = Join-Path $destination 'DungeonMapPilot'
New-Item -ItemType Directory -Path $package -Force | Out-Null
foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $sourcePath $file) -Destination (Join-Path $package $file) }
$accessPath = Join-Path $package 'Access.lua'
$access = Get-Content -LiteralPath $accessPath -Raw
if ([regex]::Matches($access, 'A\.distribution = "raidpilot-test"').Count -ne 1) { throw 'Unexpected distribution marker' }
if ($Edition -eq 'public') {
    [IO.File]::WriteAllText($accessPath, $access.Replace('A.distribution = "raidpilot-test"', 'A.distribution = "public"'), [Text.UTF8Encoding]::new($false))
}
$zip = Join-Path $destination "DungeonMapPilot-v$version-$label.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($package, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\','/') })
    $expected = @($files | ForEach-Object { "DungeonMapPilot/$_" })
    if (@(Compare-Object $entries $expected).Count -ne 0) { throw 'Unexpected ZIP content' }
} finally { $archive.Dispose() }
$hashes = @($files | ForEach-Object { @{path="DungeonMapPilot/$_"; sha256=(Get-FileHash -LiteralPath (Join-Path $package $_) -Algorithm SHA256).Hash.ToLowerInvariant()} })
@{
    name='DungeonMapPilot'; version=$version; edition=$Edition; publicRelease=$false;
    generatedAt=[DateTimeOffset]::UtcNow.ToString('o'); fileCount=$files.Count;
    zip=(Split-Path $zip -Leaf); sha256=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant();
    bytes=(Get-Item -LiteralPath $zip).Length; files=$hashes;
    scope='Package verification only; not installation, in-game validation, or publication'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'verification-locale.json') -Encoding UTF8
Write-Output "Verified $Edition package $version with $($files.Count) allowlisted files: $zip"
