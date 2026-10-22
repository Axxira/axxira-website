param([string]$SourceRoot = (Join-Path $PSScriptRoot 'source\overlay'))
$ErrorActionPreference = 'Stop'
$SourceRoot = [IO.Path]::GetFullPath($SourceRoot)
node (Join-Path $PSScriptRoot 'Restore-PublicCatalogues.cjs') $SourceRoot
if ($LASTEXITCODE -ne 0) { throw 'Public catalogue validation failed.' }
$maps=Get-Content -LiteralPath (Join-Path $SourceRoot 'LocalMap\assets\forge-manifest.json') -Raw | ConvertFrom-Json
foreach($property in $maps.maps.PSObject.Properties) {
    $map=$property.Value
    $target=[IO.Path]::GetFullPath((Join-Path $SourceRoot ('LocalMap\'+$map.path)))
    if(-not $target.StartsWith($SourceRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe image path.'}
    New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
    if((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target).Hash -eq $map.sha256){continue}
    Invoke-WebRequest -Uri $map.sourceUrl -OutFile $target -TimeoutSec 180
    if((Get-FileHash -LiteralPath $target).Hash -ne $map.sha256){throw "Published image differs from tested HOTFIX8 image: $($property.Name)"}
    Write-Host "Verified original HD map: $($property.Name)"
}

# Legacy fallback maps use the same public originals, converted locally by CI.
Add-Type -AssemblyName System.Drawing
$codec=[Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -eq 'image/jpeg'
foreach($name in 'icebreaker','the-labyrinth','the-lab') {
    $source=Join-Path $SourceRoot ('LocalMap\'+$maps.maps.$name.path)
    $target=Join-Path $SourceRoot ('OfflineMaps\'+$name+'.jpg')
    if($name -eq 'the-lab'){Copy-Item -LiteralPath $source -Destination $target -Force;continue}
    $img=[Drawing.Image]::FromFile($source)
    $parameters=[Drawing.Imaging.EncoderParameters]::new(1)
    $parameters.Param[0]=[Drawing.Imaging.EncoderParameter]::new([Drawing.Imaging.Encoder]::Quality,[long]90)
    try {$img.Save($target,$codec,$parameters)} finally {$parameters.Dispose();$img.Dispose()}
}
