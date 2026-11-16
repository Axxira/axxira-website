param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$main=Join-Path $PSScriptRoot 'source\foreverpilot'
$runtime=Join-Path $PSScriptRoot 'source\launcher\Runtime'
$mainToc=Join-Path $main 'ForeverPilot_Leveling.toc'
$version=[regex]::Match([IO.File]::ReadAllText($mainToc),'(?m)^## Version:\s*(\d+\.\d+\.\d+)').Groups[1].Value
if(!$version){throw 'FastXP version missing'}
$mainBootstrap=[IO.File]::ReadAllText((Join-Path $main 'Bootstrap.lua'))
$marker='B.distribution = "raidpilot-test"'
if([regex]::Matches($mainBootstrap,[regex]::Escape($marker)).Count -ne 1){
 throw 'Test edition marker must be exact before preparing subscriber distribution'
}
$modules=@(Get-ChildItem -LiteralPath $runtime -Directory|Where-Object{
 $_.Name -eq 'ForeverPilot_Engine' -or $_.Name -like 'ForeverPilot_Engine_*'
}|Sort-Object Name)
if($modules.Count -ne 28 -or $modules.Name -notcontains 'ForeverPilot_Engine'){
 throw "FastXP requires all 28 internal engine packs, found $($modules.Count)"
}
function ValidateToc([string]$dir,[string]$name){
 $toc=Join-Path $dir ($name+'.toc')
 if(!(Test-Path -LiteralPath $toc -PathType Leaf)){throw "Missing TOC: $name"}
 $text=[IO.File]::ReadAllText($toc)
 if($text -notmatch '(?m)^## Interface:\s*16001\s*$'){throw "Wrong interface: $name"}
 foreach($line in ($text -split '\r?\n')){
  $v=$line.Trim()
  if(!$v -or $v.StartsWith('#') -or $v -notmatch '(?i)\.(lua|xml)$'){continue}
  if($v -match '\.\.' -or [IO.Path]::IsPathRooted($v)){throw "Unsafe TOC path: $name/$v"}
  if(!(Test-Path -LiteralPath (Join-Path $dir $v) -PathType Leaf)){throw "Missing module: $name/$v"}
 }
}
ValidateToc $main 'ForeverPilot_Leveling'
if(!(Test-Path (Join-Path $main 'LICENSE_GPLv3.txt'))){throw 'GPL license missing'}
foreach($folder in $modules){ValidateToc $folder.FullName $folder.Name}
if(!(Test-Path (Join-Path $runtime 'ForeverPilot_Engine\LICENSE'))){throw 'Bundled engine license missing'}
$folders=@((Get-Item $main))+$modules
$files=@(foreach($folder in $folders){
 $archiveRoot=if($folder.FullName -eq $main){'ForeverPilot_Leveling'}else{$folder.Name}
 Get-ChildItem -LiteralPath $folder.FullName -File -Recurse|ForEach-Object{
  $relative=$_.FullName.Substring($folder.FullName.Length+1).Replace('\','/')
  [pscustomobject]@{Path=$_.FullName;Name="$archiveRoot/$relative"}
 }
})
if(@($files|Where-Object{$_.Name -match '\\|(^|/)\.\.?(/|$)'}).Count -ne 0){
 throw 'Unsafe or nonportable ZIP entry path'
}
if(@($files|Group-Object Name|Where-Object{$_.Count -ne 1}).Count -ne 0){
 throw 'Duplicate ZIP entry path'
}
if(@($files|Where-Object{$_.Name -eq 'ForeverPilot_Leveling/Bootstrap.lua'}).Count -ne 1){
 throw 'Main FastXP addon folder is missing from the archive'
}
if($files.Count -lt 660 -or $files.Count -gt 680){throw "Unexpected FastXP package count: $($files.Count)"}
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$out=Join-Path (Resolve-Path $OutputDirectory).Path "ForeverPilot-v$version.zip"
if(Test-Path -LiteralPath $out){throw "Release artifact already exists: $out"}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
function PackageBytes($entry){
 $bytes=[IO.File]::ReadAllBytes($entry.Path)
 if($entry.Name -eq 'ForeverPilot_Leveling/Bootstrap.lua'){
  $s=[Text.Encoding]::UTF8.GetString($bytes)
  $s=$s.Replace($marker,'B.distribution = "subscriber"')
  $bytes=[Text.Encoding]::UTF8.GetBytes($s)
 }elseif($entry.Name -eq 'ForeverPilot_Leveling/ForeverPilot_Leveling.toc'){
  $s=[Text.Encoding]::UTF8.GetString($bytes)
  $from='## X-FastXP-Channel: private-beta'
  if(([regex]::Matches($s,[regex]::Escape($from))).Count -ne 1){
   throw 'Unexpected TOC release channel marker'
  }
  $s=$s.Replace($from,'## X-FastXP-Channel: public-stable')
  $bytes=[Text.Encoding]::UTF8.GetBytes($s)
 }elseif($entry.Name -eq 'ForeverPilot_Leveling/README.md'){
  $s=[Text.Encoding]::UTF8.GetString($bytes)
  $oldHeading="# FastXP $version — RaidPilot (édition de test gratuite)"
  $oldIntroduction="Cette distribution du launcher RaidPilot comprend toutes les fonctions FastXP de test, sans abonnement. La distribution publique CurseForge reste séparée et continue d’utiliser le compagnon d’abonnement existant."
  if(-not $s.Contains($oldHeading) -or -not $s.Contains($oldIntroduction)){
   throw 'Subscriber package README header or introduction does not match the source version'
  }
  $s=$s.Replace($oldHeading,"# ForeversPilot FastXP $version — édition publique Free + Pro")
  $s=$s.Replace($oldIntroduction,'Cette distribution publique regroupe les modes Free et Pro dans le même addon. Le mode Free fonctionne sans abonnement; le mode Pro se déverrouille avec le compagnon Windows et une licence active. Tarif mensuel FastXP : 6,99 $ CAD. Les abonnés conservent leurs droits sans migration de compte ni dossier en double.')
  $s=$s.Replace('Cette alpha fournit','Cette édition fournit').Replace('This alpha supplies','This edition supplies')
  $bytes=[Text.Encoding]::UTF8.GetBytes($s)
 }
 return ,$bytes
}
$stream=[IO.File]::Open($out,[IO.FileMode]::CreateNew)
$z=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
try{
 foreach($file in $files){
  $entry=$z.CreateEntry($file.Name,[IO.Compression.CompressionLevel]::Optimal)
  $target=$entry.Open()
  try{
   $data=PackageBytes $file
   $target.Write($data,0,$data.Length)
  }finally{$target.Dispose()}
 }
}finally{$z.Dispose();$stream.Dispose()}
$archive=[IO.Compression.ZipFile]::OpenRead($out)
try{
 if($archive.Entries.Count -ne $files.Count){throw 'Archive entries missing'}
 $expected=@{}
 foreach($file in $files){$expected[$file.Name]=$file}
 foreach($entry in $archive.Entries){
  $file=$expected[$entry.FullName]
  if(!$file){throw "Unexpected ZIP entry $($entry.FullName)"}
  $bytes=PackageBytes $file
  $digest=[Security.Cryptography.SHA256]::Create()
  try{$expectedHash=[BitConverter]::ToString($digest.ComputeHash([byte[]]$bytes))}
  finally{$digest.Dispose()}
  $stream=$entry.Open()
  $digest=[Security.Cryptography.SHA256]::Create()
  try{$actualHash=[BitConverter]::ToString($digest.ComputeHash($stream))}
  finally{$stream.Dispose();$digest.Dispose()}
  if($expectedHash -ne $actualHash){
   throw "Archive altered: $($entry.FullName)"
  }
 }
}finally{$archive.Dispose()}
$releaseQaScript=Join-Path $PSScriptRoot 'Test-ForeverPilotPackage.ps1'
if(!(Test-Path -LiteralPath $releaseQaScript)){throw 'Missing independent FastXP package QA'}
$releaseQaOutput=@(& $releaseQaScript -ZipPath $out)
if($releaseQaOutput -notcontains 'PACKAGE_QA_PASS=True'){
 throw 'Public FastXP package QA did not confirm success'
}
[pscustomobject]@{
 Version=$version;Path=$out;Files=$files.Count;Folders=$folders.Count
 SHA256=(Get-FileHash -LiteralPath $out -Algorithm SHA256).Hash
}
