# Build-time only: restore the same open engine already shipped in our public 1.12.11.
# No installed game files, access keys or user settings are read or changed.
param([string]$ArchivePath)
$ErrorActionPreference='Stop'
$url='https://mediafilez.forgecdn.net/files/9624/583/ForeverPilot-v1.12.11.zip'
$expected='4B7D42FDFF4CDDDC4D8146D8331F712184201B9A1F79590AB18D691E50618758'
$destination=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'source/launcher/Runtime'))
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('fastxp-engine-'+[Guid]::NewGuid().ToString('N')+'.zip')
try {
 if(!$ArchivePath){Invoke-WebRequest -Uri $url -OutFile $temporary -TimeoutSec 180;$ArchivePath=$temporary}
 if((Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash -ne $expected){throw 'Engine archive does not match pinned public release'}
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $archive=[IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ArchivePath))
 try {
  $files=@($archive.Entries|Where-Object{$_.FullName -match '^ForeverPilot_Engine(?:_[A-Za-z0-9_]+)?/' -and $_.Name})
  $roots=@($files|ForEach-Object{($_.FullName -split '/')[0]}|Sort-Object -Unique)
  if($files.Count -ne 608 -or $roots.Count -ne 28){throw 'Incomplete engine snapshot'}
  if(Test-Path -LiteralPath $destination){
   if((Get-Item -LiteralPath $destination).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Runtime cannot be a reparse point'}
   foreach($item in Get-ChildItem -LiteralPath $destination -Recurse -Force){
    if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Runtime contains a reparse point'}
   }
  }
  foreach($entry in $files){
   if($entry.FullName -match '\\|(^|/)\.\.?(/|$)|:'){throw 'Unsafe engine entry'}
   $target=[IO.Path]::GetFullPath((Join-Path $destination $entry.FullName))
   if(!$target.StartsWith($destination+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Engine path escaped generated directory'}
   $inputStream=$entry.Open();$digest=[Security.Cryptography.SHA256]::Create()
   try{$hash=[BitConverter]::ToString($digest.ComputeHash($inputStream)).Replace('-','')}
   finally{$inputStream.Dispose();$digest.Dispose()}
   if(Test-Path -LiteralPath $target){
    if((Get-FileHash -LiteralPath $target).Hash -ne $hash){throw "Local engine changes must be preserved: $($entry.FullName)"}
   }else{
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$target,$false)
    if((Get-FileHash -LiteralPath $target).Hash -ne $hash){throw "Engine copy differs: $($entry.FullName)"}
   }
  }
  if(@(Get-ChildItem -LiteralPath $destination -File -Recurse).Count -ne 608){throw 'Unexpected generated engine files'}
  foreach($root in $roots){if(!(Test-Path -LiteralPath (Join-Path $destination "$root/$root.toc"))){throw "Missing engine TOC: $root"}}
  if(!(Test-Path -LiteralPath (Join-Path $destination 'ForeverPilot_Engine/LICENSE'))){throw 'Engine license missing'}
  Write-Host 'Verified engine restored: 608 files / 28 internal folders; public archive SHA256 matched.'
 }finally{$archive.Dispose()}
}finally{
 if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary -Force}
}
