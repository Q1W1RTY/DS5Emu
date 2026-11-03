$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.Drawing
$destination=Join-Path $root 'src\DS5Link.App\Assets'
New-Item -ItemType Directory $destination -Force|Out-Null
function Convert-PngIcon([string]$Source,[string]$Output,[int[]]$Sizes){
 $image=[Drawing.Image]::FromFile($Source)
 try{
  $frames=foreach($size in $Sizes){
   $bitmap=[Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
   $graphics=[Drawing.Graphics]::FromImage($bitmap);$stream=[IO.MemoryStream]::new()
   try{
    $graphics.Clear([Drawing.Color]::Transparent)
    $graphics.CompositingMode=[Drawing.Drawing2D.CompositingMode]::SourceCopy
    $graphics.CompositingQuality=[Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.DrawImage($image,[Drawing.Rectangle]::new(0,0,$size,$size),0,0,$image.Width,$image.Height,[Drawing.GraphicsUnit]::Pixel)
    $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
    [pscustomobject]@{size=$size;data=$stream.ToArray()}
   }finally{$graphics.Dispose();$stream.Dispose();$bitmap.Dispose()}
  }
  $writer=[IO.BinaryWriter]::new([IO.File]::Create($Output))
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
 }finally{$image.Dispose()}
}
# Package generated artwork without redrawing it; retain transparent alpha.
Convert-PngIcon (Join-Path $root 'assets\brand\DS5Link.png') (Join-Path $destination 'DS5Link.ico') @(16,20,24,32,40,48,64,128,256)
Convert-PngIcon (Join-Path $root 'assets\brand\DS5Link-tray.png') (Join-Path $destination 'DS5Link.Tray.ico') @(16,20,24,32,40,48,64)
Write-Host 'Packaged generated app and dedicated tray artwork into multiresolution ICO files.'
