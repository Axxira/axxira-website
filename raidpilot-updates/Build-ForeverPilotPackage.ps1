param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$addonRoot = Join-Path $PSScriptRoot 'source/foreverpilot'
$toc = Get-Content -LiteralPath (Join-Path $addonRoot 'ForeverPilot_Leveling.toc') -Raw
$version = [regex]::Match($toc, '(?m)^## Version:\s*(\d+\.\d+\.\d+)').Groups[1].Value
if (!$version) { throw 'Addon version is missing.' }
$expected = @('ForeverPilot_Leveling.toc', 'README.md', 'LICENSE_GPLv3.txt', 'Media/Panel.tga')
$expected += @($toc -split '\r?\n' | Where-Object { $_ -match '^\w+\.lua$' })
foreach ($relative in $expected) {
    if (!(Test-Path -LiteralPath (Join-Path $addonRoot $relative) -PathType Leaf)) { throw "Missing addon file: $relative" }
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$zipPath = Join-Path (Resolve-Path $OutputDirectory).Path "ForeverPilot-v$version.zip"
if (Test-Path -LiteralPath $zipPath) { throw 'Refusing to overwrite an existing publication archive.' }
Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
$archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($relative in $expected) {
        $entry = $archive.CreateEntry(('ForeverPilot_Leveling/' + $relative.Replace('\','/')), [IO.Compression.CompressionLevel]::Optimal)
        $inputStream = [IO.File]::OpenRead((Join-Path $addonRoot $relative))
        $outputStream = $entry.Open()
        try { $inputStream.CopyTo($outputStream) } finally { $inputStream.Dispose(); $outputStream.Dispose() }
    }
} finally { $archive.Dispose(); $stream.Dispose() }
$check = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    if ($check.Entries.Count -ne $expected.Count) { throw 'Package file count mismatch.' }
    foreach ($entry in $check.Entries) {
        if (!$entry.FullName.StartsWith('ForeverPilot_Leveling/')) { throw 'Unexpected archive root.' }
        $relative = $entry.FullName.Substring('ForeverPilot_Leveling/'.Length)
        $entryStream=$entry.Open()
        try { $actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($entryStream)) } finally { $entryStream.Dispose() }
        if ($actual -ne (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $addonRoot $relative)).Hash) { throw "Archive differs: $relative" }
    }
} finally { $check.Dispose() }
[pscustomobject]@{Version=$version;Path=$zipPath;Files=$expected.Count;SHA256=(Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash}
