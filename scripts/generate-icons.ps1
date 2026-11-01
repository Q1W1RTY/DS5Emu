$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.Drawing
Add-Type -Path (Join-Path $root 'assets\brand\IconArtwork.cs') -ReferencedAssemblies System.Drawing
$destination=Join-Path $root 'src\DSEmu.App\Assets'
New-Item -ItemType Directory $destination -Force|Out-Null
$sizes=@(16,20,24,32,40,48,64,128,256)
$frames=foreach($size in $sizes){
 $bitmap=[IconArtwork]::Render($size);$stream=[IO.MemoryStream]::new()
 try{$bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png);[pscustomobject]@{size=$size;data=$stream.ToArray()}}
 finally{$stream.Dispose();$bitmap.Dispose()}
}
$writer=[IO.BinaryWriter]::new([IO.File]::Create((Join-Path $destination 'DSEmu.ico')))
try{
 $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count)
 $offset=6+16*$frames.Count
 foreach($frame in $frames){
  $dimension=if($frame.size -eq 256){0}else{$frame.size}
  $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0)
  $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frame.data.Length);$writer.Write([uint32]$offset)
  $offset+=$frame.data.Length
 }
 foreach($frame in $frames){$writer.Write([byte[]]$frame.data)}
}finally{$writer.Dispose()}
$preview=[IconArtwork]::Render(256)
try{$preview.Save((Join-Path $root 'assets\brand\DSEmu.png'),[Drawing.Imaging.ImageFormat]::Png)}finally{$preview.Dispose()}
Write-Host 'Generated original DSEmu icon: 16..256 px, 9 sizes.'
