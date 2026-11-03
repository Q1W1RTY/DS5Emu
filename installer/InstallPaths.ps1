# Shared path rules for setup, upgrades and removal. No filesystem mutations here.
function Get-DS5RegisteredInstall {
 foreach($key in @('HKLM:\SOFTWARE\DS5Link','HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DS5Link')){
  $value=(Get-ItemProperty -LiteralPath $key -Name InstallLocation -ErrorAction SilentlyContinue).InstallLocation
  if($value){return [IO.Path]::GetFullPath($value).TrimEnd('\')}
 }
 return $null
}
function Resolve-DS5InstallPath([string]$Requested) {
 $registered=Get-DS5RegisteredInstall
 if(!$Requested){$Requested=if($registered){$registered}else{Join-Path $env:ProgramFiles 'DS5Link'}}
 if($Requested -notmatch '^[A-Za-z]:\\' -or $Requested -match '["<>|?*\x00-\x1f]'){throw 'Укажите полный путь на локальном диске, например D:\Apps\DS5Link.'}
 $resolved=[IO.Path]::GetFullPath($Requested).TrimEnd('\')
 $root=[IO.Path]::GetPathRoot($resolved)
 if($resolved.TrimEnd('\') -eq $root.TrimEnd('\')){throw 'Нельзя устанавливать приложение прямо в корень диска.'}
 foreach($part in $resolved.Substring($root.Length).Split('\')){
  if($part -match '[. ]$' -or $part -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)'){throw 'Недопустимое имя папки установки.'}
 }
 foreach($blocked in @($env:windir,$env:ProgramData,[IO.Path]::GetDirectoryName($env:USERPROFILE))){
  if(!$blocked){continue};$blocked=[IO.Path]::GetFullPath($blocked).TrimEnd('\')
  if($resolved.Equals($blocked,[StringComparison]::OrdinalIgnoreCase) -or $resolved.StartsWith($blocked+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Выберите отдельную папку вне Windows, ProgramData и профилей пользователей.'}
 }
 foreach($blocked in @($env:ProgramFiles,${env:ProgramFiles(x86)})){
  if($blocked -and $resolved.Equals($blocked.TrimEnd('\'),[StringComparison]::OrdinalIgnoreCase)){throw 'Укажите отдельную папку приложения, а не общий каталог программ.'}
 }
 if($registered -and !$resolved.Equals($registered,[StringComparison]::OrdinalIgnoreCase)){throw "DS5Link уже установлен в $registered. Для переноса сначала удалите приложение; настройки будут сохранены."}
 return $resolved
}
function Assert-DS5NoReparse([string]$Path) {
 $cursor=[IO.Path]::GetFullPath($Path)
 while($cursor){
  if(Test-Path -LiteralPath $cursor){
   $item=Get-Item -LiteralPath $cursor -Force
   if(!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw "Путь установки содержит файл или ссылку: $cursor"}
  }
  $cursor=[IO.Path]::GetDirectoryName($cursor)
 }
}
function Assert-DS5InstallTarget([string]$Path) {
 $drive=[IO.DriveInfo]::new([IO.Path]::GetPathRoot($Path))
 if(!$drive.IsReady -or $drive.DriveType -ne [IO.DriveType]::Fixed -or $drive.DriveFormat -notin @('NTFS','ReFS')){throw 'Выберите локальный постоянный диск с поддержкой прав доступа NTFS/ReFS.'}
 Assert-DS5NoReparse $Path
 $registered=Get-DS5RegisteredInstall
 if((Test-Path -LiteralPath $Path) -and (!$registered -or !$Path.Equals($registered,[StringComparison]::OrdinalIgnoreCase)) -and @(Get-ChildItem -LiteralPath $Path -Force).Count){throw 'Выбранная папка не пуста. Создайте отдельную пустую папку для DS5Link.'}
 # The executable runs as SYSTEM: writable/replaceable ancestors are unsafe.
 $trusted=@('S-1-5-18','S-1-5-32-544','S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')
 $cursor=[IO.Path]::GetDirectoryName($Path)
 while($cursor){
  if(Test-Path -LiteralPath $cursor){
   $acl=Get-Acl -LiteralPath $cursor
   if($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $trusted){throw "Родительская папка доступна владельцу без повышения прав: $cursor. Выберите защищённый каталог, например отдельную папку в корне диска или Program Files."}
   $isRoot=$cursor.TrimEnd('\') -eq [IO.Path]::GetPathRoot($cursor).TrimEnd('\')
   $dangerous=[int64]([Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor [Security.AccessControl.FileSystemRights]::ChangePermissions -bor [Security.AccessControl.FileSystemRights]::TakeOwnership)
   if(!$isRoot){$dangerous=$dangerous -bor [int64][Security.AccessControl.FileSystemRights]::Delete}
   foreach($rule in $acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])){
    if($rule.AccessControlType -ne 'Allow' -or ($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) -or $rule.IdentityReference.Value -in $trusted){continue}
    $rights=[int64]$rule.FileSystemRights
    if(($rights -band $dangerous) -or ($rights -band 0x10000000)){throw "Родительская папка допускает подмену файлов службы: $cursor. Выберите защищённую папку."}
   }
  }
  $cursor=[IO.Path]::GetDirectoryName($cursor)
 }
}
