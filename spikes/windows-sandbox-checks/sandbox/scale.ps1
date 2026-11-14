# Runs inside Windows Sandbox: sets the display scale (append ! to bypass the reported range), waits, records the
# result and a screenshot. Guest only; the exe refuses outside the Sandbox account.
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Scale, [string]$Out = 'scale')
$outDir = Join-Path 'C:\spar\out' $Out
New-Item -ItemType Directory -Force $outDir | Out-Null
$file = Join-Path $outDir 'scale.txt'
Start-Process -FilePath $Exe -ArgumentList @('--scale', $Scale, $file) -Wait
Start-Sleep -Seconds 3
Start-Process -FilePath $Exe -ArgumentList @('--scale', 'get', $file) -Wait
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
foreach ($s in [System.Windows.Forms.Screen]::AllScreens) { "screen $($s.Bounds)" | Out-File $file -Append -Encoding utf8 }
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
$g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
$bmp.Save((Join-Path $outDir "screen-$($Scale.TrimEnd('!')).png"))
