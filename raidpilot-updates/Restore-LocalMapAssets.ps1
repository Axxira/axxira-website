param([string]$SourceRoot = (Join-Path $PSScriptRoot 'source\overlay'))
$ErrorActionPreference = 'Stop'
$SourceRoot = [IO.Path]::GetFullPath($SourceRoot)

node (Join-Path $PSScriptRoot 'Restore-PublicCatalogues.cjs') $SourceRoot
if ($LASTEXITCODE -ne 0) { throw 'Public catalogue validation failed.' }

# Immutable fallback: the previous tested release contains the exact same
# credited, pinned public map images. Never trust an upstream silent replacement.
$testedArchiveUrl = 'https://github.com/Axxira/axxira-website/releases/download/raidpilot-stable-rp0.14.2.15-b0.3.4-l0.2.12-4d651c1b68e6/RaidPilot-Overlay-v0.14.2.15-Windows-x64.zip'
$testedArchiveSha256 = '43ae4e179a6803298cbe00dde928680d3817a04e4eff10a9a5c3e75878cc9299'
$cacheRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { [IO.Path]::GetTempPath() } else { $env:RUNNER_TEMP }
$testedArchivePath = Join-Path $cacheRoot 'RaidPilot-Tested-Map-Assets-0.14.2.15.zip'

function Restore-ImmutableMap {
    param(
        [Parameter(Mandatory=$true)][string]$RelativePath,
        [Parameter(Mandatory=$true)][string]$Destination,
        [Parameter(Mandatory=$true)][string]$ExpectedSha256
    )

    if (-not (Test-Path -LiteralPath $testedArchivePath) -or
        (Get-FileHash -Algorithm SHA256 -LiteralPath $testedArchivePath).Hash -ne $testedArchiveSha256) {
        Write-Host 'Downloading last tested RaidPilot release to recover pinned original map image(s).'
        Invoke-WebRequest -Uri $testedArchiveUrl -OutFile $testedArchivePath -TimeoutSec 240
        if ((Get-FileHash -Algorithm SHA256 -LiteralPath $testedArchivePath).Hash -ne $testedArchiveSha256) {
            throw 'Last tested release archive SHA-256 mismatch: image recovery aborted.'
        }
    }

    Add-Type -AssemblyName System.IO.Compression
    $archive = [IO.Compression.ZipFile]::OpenRead($testedArchivePath)
    $sourceStream = $null
    $destinationStream = $null
    try {
        $expectedEntry = 'LocalMap/' + $RelativePath.Replace('\','/')
        $found = @($archive.Entries | Where-Object {
            $_.FullName.Replace('\','/').Equals(
                $expectedEntry, [StringComparison]::OrdinalIgnoreCase)
        })
        if ($found.Count -ne 1) {
            throw "Pinned image absent or ambiguous in immutable release: $expectedEntry"
        }

        $sourceStream = $found[0].Open()
        $destinationStream = [IO.File]::Open(
            $Destination, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $sourceStream.CopyTo($destinationStream)
    }
    finally {
        if ($destinationStream -ne $null) { $destinationStream.Dispose() }
        if ($sourceStream -ne $null) { $sourceStream.Dispose() }
        $archive.Dispose()
    }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $Destination).Hash -ne $ExpectedSha256) {
        throw "Pinned original image checksum mismatch after restore: $RelativePath"
    }
}

$maps = Get-Content -LiteralPath (Join-Path $SourceRoot 'LocalMap\assets\forge-manifest.json') -Raw | ConvertFrom-Json
foreach ($property in $maps.maps.PSObject.Properties) {
    $map = $property.Value
    $target = [IO.Path]::GetFullPath((Join-Path $SourceRoot ('LocalMap\' + $map.path)))
    if (-not $target.StartsWith($SourceRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Unsafe image path.'
    }
    New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
    if ((Test-Path -LiteralPath $target) -and
        (Get-FileHash -Algorithm SHA256 -LiteralPath $target).Hash -eq $map.sha256) {
        continue
    }

    $downloadedOriginal = $false
    try {
        Invoke-WebRequest -Uri $map.sourceUrl -OutFile $target -TimeoutSec 180
        $downloadedOriginal =
            (Get-FileHash -Algorithm SHA256 -LiteralPath $target).Hash -eq $map.sha256
    }
    catch {
        Write-Warning "Original image source unavailable for $($property.Name): using pinned fallback."
    }
    if (-not $downloadedOriginal) {
        Write-Warning "Source for $($property.Name) differs from its tested image: recovering immutable original."
        Restore-ImmutableMap -RelativePath $map.path -Destination $target -ExpectedSha256 $map.sha256
    }

    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $target).Hash -ne $map.sha256) {
        throw "Image failed SHA-256 verification even after tested-release recovery: $($property.Name)"
    }
    Write-Host "Verified original HD map: $($property.Name)"
}

# Legacy fallback maps reuse exactly the same verified originals.
Add-Type -AssemblyName System.Drawing
$codec = [Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -eq 'image/jpeg'
foreach ($name in 'icebreaker','the-labyrinth','the-lab') {
    $source = Join-Path $SourceRoot ('LocalMap\' + $maps.maps.$name.path)
    $target = Join-Path $SourceRoot ('OfflineMaps\' + $name + '.jpg')
    if ($name -eq 'the-lab') { Copy-Item -LiteralPath $source -Destination $target -Force; continue }
    $img = [Drawing.Image]::FromFile($source)
    $parameters = [Drawing.Imaging.EncoderParameters]::new(1)
    $parameters.Param[0] = [Drawing.Imaging.EncoderParameter]::new([Drawing.Imaging.Encoder]::Quality,[long]90)
    try { $img.Save($target,$codec,$parameters) } finally { $parameters.Dispose(); $img.Dispose() }
}
