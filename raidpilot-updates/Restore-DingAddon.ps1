# Build-time dependency only. End users receive Ding inside the verified launcher archive.
$ErrorActionPreference = 'Stop'
$archiveUrl = 'https://github.com/moust4ki/DingGuideWoWForever/releases/download/v1.8.1/Ding-v1.8.1-forever.zip'
$archiveSha256 = 'D9707C45F42DC95429129E4F476E07D37EE90D6E07E6E84256B390EF685285EA'
$destination = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'source/ding'))
$work = Join-Path ([IO.Path]::GetTempPath()) ('raidpilot-ding-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $archive = Join-Path $work 'Ding.zip'
    Invoke-WebRequest -Uri $archiveUrl -OutFile $archive
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $archiveSha256) {
        throw 'Ding download checksum differs from the pinned official release.'
    }
    $unpacked = Join-Path $work 'unpacked'
    Expand-Archive -LiteralPath $archive -DestinationPath $unpacked
    $source = Join-Path $unpacked 'Ding'
    foreach ($required in @('Ding.toc', 'LICENSE', 'DB/CREDITS.md', 'Core/Ding.lua', 'Core/Data.lua', 'Core/Registry.lua', 'Media/Textures/arrow.tga')) {
        if (!(Test-Path -LiteralPath (Join-Path $source $required))) { throw "Ding release is incomplete: $required" }
    }
    if (Test-Path -LiteralPath $destination) {
        # This exact generated directory belongs to this build; never remove the installed game addon.
        $expected = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'source/ding'))
        if ((Resolve-Path -LiteralPath $destination).Path -ne $expected -or
            ((Get-Item -LiteralPath $destination).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Refusing to replace an unexpected Ding build directory.'
        }
        Remove-Item -LiteralPath $destination -Recurse -Force
    }
    Copy-Item -LiteralPath $source -Destination $destination -Recurse
    $files = @(Get-ChildItem -LiteralPath $source -File -Recurse)
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($source, $file.FullName)
        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne
            (Get-FileHash -LiteralPath (Join-Path $destination $relative)).Hash) { throw "Ding copy differs: $relative" }
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Ding-NOTICE.txt') -Destination (Join-Path $destination 'RAIDPILOT-NOTICE.txt')
    Write-Host "Ding v1.8.1 restored: $($files.Count) original files, checksum verified, GPLv3 and credits included."
} finally {
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ((Test-Path -LiteralPath $work) -and [IO.Path]::GetFullPath($work).StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($work).StartsWith('raidpilot-ding-')) { Remove-Item -LiteralPath $work -Recurse -Force }
}
