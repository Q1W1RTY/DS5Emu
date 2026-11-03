# File ownership for uninstall. Never remove an installation tree recursively.
function Assert-AppOwnedPath([string]$Root,[string]$Relative) {
 $rootPath=[IO.Path]::GetFullPath($Root).TrimEnd('\')
 if(!$rootPath -or $rootPath.Equals([IO.Path]::GetPathRoot($rootPath).TrimEnd('\'),[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe application root'}
 $relativePath=$Relative.Replace('/','\')
 if(!$relativePath -or [IO.Path]::IsPathRooted($relativePath) -or $relativePath -match '[:*?"<>|\x00-\x1f]'){throw 'Invalid owned file path'}
 foreach($part in $relativePath.Split('\')){if(!$part -or $part -in @('.','..') -or $part -match '[. ]$'){throw 'Invalid owned path component'}}
 $full=[IO.Path]::GetFullPath((Join-Path $rootPath $relativePath))
 if(!$full.StartsWith($rootPath+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Owned file escapes the application root'}
 $cursor=$full
 while($cursor -and ($cursor.Equals($rootPath,[StringComparison]::OrdinalIgnoreCase) -or $cursor.StartsWith($rootPath+'\',[StringComparison]::OrdinalIgnoreCase))){
  if(Test-Path -LiteralPath $cursor){if((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Refusing linked application path: $cursor"}}
  if($cursor.Equals($rootPath,[StringComparison]::OrdinalIgnoreCase)){break};$cursor=[IO.Path]::GetDirectoryName($cursor)
 }
 return $full
}
function New-AppOwnedManifest([string]$Root,[string]$Product='DS5Link') {
 $rootPath=[IO.Path]::GetFullPath($Root).TrimEnd('\')
 $files=@(Get-ChildItem -LiteralPath $rootPath -File -Recurse -Force | Where-Object Name -ne 'installed-files.json' | ForEach-Object {
  $relative=$_.FullName.Substring($rootPath.Length+1);$null=Assert-AppOwnedPath $rootPath $relative
  @{path=$relative;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
 })
 if(!$files.Count){throw 'Application payload is empty'}
 @{schema=1;product=$Product;files=$files}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $rootPath 'installed-files.json') -Encoding utf8
}
function Get-AppOwnedRemovalPlan([string]$Root,[string]$Product='DS5Link') {
 $rootPath=[IO.Path]::GetFullPath($Root).TrimEnd('\');$manifest=Assert-AppOwnedPath $rootPath 'installed-files.json'
 if(!(Test-Path -LiteralPath $manifest -PathType Leaf) -or (Get-Item -LiteralPath $manifest).Length -gt 4194304){throw 'Missing or oversized installed-file manifest. Application files retained.'}
 $record=Get-Content -LiteralPath $manifest -Raw|ConvertFrom-Json
 if($record.schema -ne 1 -or $record.product -ne $Product -or !@($record.files).Count){throw 'Invalid installed-file manifest. Application files retained.'}
 $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);$owned=@();$retained=@();$directories=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
 foreach($file in $record.files){
  $relative=[string]$file.path;$full=Assert-AppOwnedPath $rootPath $relative
  if(!$seen.Add($full) -or $relative -eq 'installed-files.json' -or $file.sha256 -notmatch '^[0-9a-fA-F]{64}$'){throw 'Invalid or duplicate owned file. Application files retained.'}
  if(Test-Path -LiteralPath $full){
   if(!(Test-Path -LiteralPath $full -PathType Leaf)){throw 'Owned file was replaced by a directory. Application files retained.'}
   if((Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash -eq $file.sha256){$owned+=@{path=$full;relative=$relative;sha256=$file.sha256}}else{$retained+=$full}
  }
  $parent=[IO.Path]::GetDirectoryName($full)
  while($parent -and !$parent.Equals($rootPath,[StringComparison]::OrdinalIgnoreCase)){$null=$directories.Add($parent);$parent=[IO.Path]::GetDirectoryName($parent)}
 }
 [pscustomobject]@{root=$rootPath;manifest=$manifest;manifestHash=(Get-FileHash -LiteralPath $manifest).Hash;files=$owned;retained=$retained;directories=@($directories)}
}
function Remove-AppOwnedFiles($Plan) {
 if((Get-FileHash -LiteralPath (Assert-AppOwnedPath $Plan.root 'installed-files.json')).Hash -ne $Plan.manifestHash){throw 'Ownership manifest changed. Files retained.'}
 $removed=0;$retained=@($Plan.retained)
 foreach($file in ($Plan.files|Sort-Object @{Expression={if($_.relative -in @('Uninstall.ps1','InstallPaths.ps1','OwnedFiles.ps1')){1}else{0}}},relative)){
  $path=Assert-AppOwnedPath $Plan.root $file.relative
  if(!(Test-Path -LiteralPath $path)){continue}
  if((Get-FileHash -LiteralPath $path).Hash -ne $file.sha256){$retained+=$path;continue}
  $deadline=[DateTime]::UtcNow.AddSeconds(10)
  do{try{Remove-Item -LiteralPath $path -Force -ErrorAction Stop;$removed++;break}catch [IO.IOException]{if([DateTime]::UtcNow -ge $deadline){throw};Start-Sleep -Milliseconds 200}}while($true)
 }
 $null=Assert-AppOwnedPath $Plan.root 'installed-files.json';Remove-Item -LiteralPath $Plan.manifest -Force
 foreach($directory in ($Plan.directories|Sort-Object Length -Descending)){
  $relative=$directory.Substring($Plan.root.Length+1);$null=Assert-AppOwnedPath $Plan.root $relative
  try{[IO.Directory]::Delete($directory,$false)}catch [IO.IOException]{}catch [UnauthorizedAccessException]{}
 }
 # Directory.Delete(false) succeeds only if nothing else remains in the folder.
 try{[IO.Directory]::Delete($Plan.root,$false)}catch [IO.IOException]{}catch [UnauthorizedAccessException]{}
 [pscustomobject]@{removed=$removed;retained=$retained;rootRemaining=(Test-Path -LiteralPath $Plan.root)}
}
