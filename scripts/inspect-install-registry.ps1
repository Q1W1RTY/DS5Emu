$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$result=@{admin=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)}
try{
 $key=[Microsoft.Win32.Registry]::LocalMachine.CreateSubKey('SOFTWARE\DS5Link',$true)
 $result.opened=$true
 $key.SetValue('OwnerSid',[Security.Principal.WindowsIdentity]::GetCurrent().User.Value,[Microsoft.Win32.RegistryValueKind]::String)
 $key.Close();$result.written=$true
}catch{$result.error=$_.Exception.Message}
$result|ConvertTo-Json|Set-Content (Join-Path $root 'artifacts\registry-inspection.json') -Encoding utf8
