$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
# Only crop, resample and export the generated raster files; no artwork is drawn.
Add-Type -TypeDefinition @'
using System;
public static class AlphaInspector {
  public static int[] Bounds(byte[] p, int width, int height) {
    int stride=width*4; int[] rows=new int[height], cols=new int[width];
    for(int y=0;y<height;y++) for(int x=0;x<width;x++) if(p[y*stride+x*4+3]>8){ rows[y]++;cols[x]++; }
    int left=0,right=width-1,top=0,bottom=height-1;
    while(left<right && cols[left]<12)left++; while(right>left && cols[right]<12)right--;
    while(top<bottom && rows[top]<12)top++; while(bottom>top && rows[bottom]<12)bottom--;
    left=Math.Max(0,left-4);right=Math.Min(width-1,right+4);top=Math.Max(0,top-4);bottom=Math.Min(height-1,bottom+4);
    return new int[]{left,top,right-left+1,bottom-top+1};
  }
  public static long[] Stats(byte[] p) {
    long clear=0,partial=0,opaque=0;
    for(int i=3;i<p.Length;i+=4){if(p[i]==0)clear++;else if(p[i]==255)opaque++;else partial++;}
    return new long[]{clear,partial,opaque};
  }
}
'@
function Get-RasterPixels([System.Drawing.Bitmap]$bitmap) {
  $data=$bitmap.LockBits([System.Drawing.Rectangle]::new(0,0,$bitmap.Width,$bitmap.Height),[System.Drawing.Imaging.ImageLockMode]::ReadOnly,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  try {
    $bytes=[byte[]]::new([Math]::Abs($data.Stride)*$bitmap.Height)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0,$bytes,0,$bytes.Length)
    return ,$bytes
  } finally { $bitmap.UnlockBits($data) }
}
$root = 'C:\Users\pedro\AcerMode\design'
$assets = Get-Content -LiteralPath (Join-Path $root 'tmp\imagegen\sources.json') -Raw | ConvertFrom-Json
$report = foreach ($a in $assets) {
  $destination = Join-Path $root $a.name
  if (Test-Path -LiteralPath $destination) { throw "Destination already exists: $destination" }
  $source = [System.Drawing.Bitmap]::new($a.source)
  try {
    if ($a.padding -gt 0) { $box = [AlphaInspector]::Bounds((Get-RasterPixels $source),$source.Width,$source.Height); $bounds = [System.Drawing.Rectangle]::new($box[0],$box[1],$box[2],$box[3]) }
    else { $bounds = [System.Drawing.Rectangle]::new(0,0,$source.Width,$source.Height) }
    $canvas = [System.Drawing.Bitmap]::new(($a.width + 2*$a.padding),($a.height + 2*$a.padding),[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
      $g = [System.Drawing.Graphics]::FromImage($canvas)
      try {
        $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $drawWidth=[double]$a.width; $drawHeight=[double]$a.height
        if ($a.preserveAspect) {
          $scale=[Math]::Min($a.width/[double]$bounds.Width,$a.height/[double]$bounds.Height)
          $drawWidth=$bounds.Width*$scale; $drawHeight=$bounds.Height*$scale
        }
        $rect = [System.Drawing.RectangleF]::new([single](($canvas.Width-$drawWidth)/2),[single](($canvas.Height-$drawHeight)/2),[single]$drawWidth,[single]$drawHeight)
        $sourceRect=[System.Drawing.RectangleF]::new($bounds.X,$bounds.Y,$bounds.Width,$bounds.Height)
        $g.DrawImage($source,$rect,$sourceRect,[System.Drawing.GraphicsUnit]::Pixel)
      } finally { $g.Dispose() }
      $canvas.Save($destination,[System.Drawing.Imaging.ImageFormat]::Png)
      $stats=[AlphaInspector]::Stats((Get-RasterPixels $canvas))
      if ($a.name -ne 'ui-mockup.png' -and $stats[0] -eq 0) { throw "Missing real transparency: $($a.name)" }
      [pscustomobject]@{File=$a.name;Width=$canvas.Width;Height=$canvas.Height;TransparentPixels=$stats[0];PartialAlphaPixels=$stats[1];OpaquePixels=$stats[2];SourceCrop="$($bounds.X),$($bounds.Y),$($bounds.Width),$($bounds.Height)"}
    } finally { $canvas.Dispose() }
  } finally { $source.Dispose() }
}
$report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'tmp\imagegen\validation.json') -Encoding utf8
$report | Format-Table -AutoSize
