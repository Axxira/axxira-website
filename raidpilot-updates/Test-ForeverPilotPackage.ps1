param([Parameter(Mandatory=$true)][string]$ZipPath)
$ErrorActionPreference='Stop'
if(!(Test-Path -LiteralPath $ZipPath -PathType Leaf)){throw 'Package ZIP missing'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$z=[IO.Compression.ZipFile]::OpenRead($ZipPath)
try{
 $entries=@($z.Entries)
 if($entries.Count -ne 669){throw "Unexpected package file count: $($entries.Count)"}
 $names=@($entries|ForEach-Object{$_.FullName})
 if(@($names|Group-Object|Where-Object{$_.Count -ne 1}).Count){throw 'Duplicate ZIP entry names'}
 if(@($names|Where-Object{$_ -match '(^/|\\|(^|/)\.\.?(/|$)|//|^[A-Za-z]:)'}).Count){throw 'Unsafe or nonportable ZIP paths'}
 $roots=@($names|ForEach-Object{($_ -split '/')[0]}|Sort-Object -Unique)
 if($roots.Count -ne 29 -or $roots -notcontains 'ForeverPilot_Leveling' -or
    $roots -contains 'foreverpilot'){throw 'Invalid main addon ZIP directory'}
 $engines=@($roots|Where-Object{$_ -eq 'ForeverPilot_Engine' -or $_ -like 'ForeverPilot_Engine_*'})
 if($engines.Count -ne 28){throw "Missing internal engine packs: $($engines.Count)"}
 $lookup=@{}
 foreach($e in $entries){$lookup[$e.FullName]=$e}
 function ReadText([string]$name){
  if(-not $lookup.ContainsKey($name)){throw "Missing ZIP entry: $name"}
  $r=[IO.StreamReader]::new($lookup[$name].Open(),[Text.Encoding]::UTF8)
  try{return $r.ReadToEnd()}finally{$r.Dispose()}
 }
 $main='ForeverPilot_Leveling'
 $bootstrap=ReadText "$main/Bootstrap.lua"
 if(-not $bootstrap.Contains('B.distribution = "subscriber"') -or
    $bootstrap.Contains('B.distribution = "raidpilot-test"')){
    throw 'Public ZIP was not converted to unified Free/Pro distribution'
 }
 $readme=ReadText "$main/README.md"
 if(-not $readme.Contains('# ForeversPilot FastXP 1.12.12') -or
    -not $readme.Contains('Free + Pro') -or
    -not $readme.Contains('6,99 $ CAD') -or
    $readme.Contains('Cette alpha fournit')){
    throw 'Public ZIP metadata is still labeled as a test build'
 }
 $toc=ReadText "$main/$main.toc"
 if(-not $toc.Contains('## Version: 1.12.12')){throw 'Incorrect main TOC version'}
 if(-not $toc.Contains('## X-FastXP-Channel: public-stable') -or
    $toc.Contains('## X-FastXP-Channel: private-beta')){
    throw 'Published main TOC still claims to be a private beta'
 }
 foreach($root in $roots){
  $tocName="$root/$root.toc"
  $tocBody=ReadText $tocName
  foreach($line in ($tocBody -split '\r?\n')){
   $module=$line.Trim()
   if(-not $module -or $module.StartsWith('#') -or
      $module -notmatch '(?i)\.(lua|xml)$'){continue}
   $member="$root/"+$module.Replace('\','/')
   if(-not $lookup.ContainsKey($member)){throw "Missing TOC entry: $member"}
  }
 }
 'PACKAGE_QA_PASS=True'
 'ZIP_ENTRIES='+$entries.Count
 'ADDON_FOLDERS='+$roots.Count
 'INTERNAL_ENGINE_PACKS='+$engines.Count
 'UNIFIED_FREE_PRO=True'
 'ALL_TOC_REFERENCES_PRESENT=True'
 'ZIP_SHA256='+(Get-FileHash $ZipPath -Algorithm SHA256).Hash
}finally{$z.Dispose()}
