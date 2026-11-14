# Runs inside Windows Sandbox: records the session, display, OS build, WebView2 runtime folders, the camera and
# microphone privacy settings (read-only registry reads) and whether Narrator exists, then a screenshot.
param([Parameter(Mandatory)][string]$Exe, [string]$Out = 'info')
$outDir = Join-Path 'C:\spar\out' $Out
New-Item -ItemType Directory -Force $outDir | Out-Null
$log = Join-Path $outDir 'info.txt'
"user=$env:USERNAME session=$((Get-Process -Id $PID).SessionId)" | Out-File $log
$os = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
"os=$($os.ProductName) $($os.DisplayVersion) build=$($os.CurrentBuild).$($os.UBR)" | Out-File $log -Append
Get-ChildItem 'C:\Program Files (x86)\Microsoft\EdgeWebView\Application' -Directory -ErrorAction SilentlyContinue |
  ForEach-Object { "webview2 folder=$($_.Name)" } | Out-File $log -Append
Start-Process -FilePath $Exe -ArgumentList @('--scale', 'get', (Join-Path $outDir 'scale.txt')) -Wait
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
foreach ($s in [System.Windows.Forms.Screen]::AllScreens) { "screen $($s.Bounds) primary=$($s.Primary)" | Out-File $log -Append }
foreach ($cap in 'webcam', 'microphone') {
  foreach ($hive in 'HKLM', 'HKCU') {
    $key = "${hive}:\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\$cap"
    $v = (Get-ItemProperty $key -ErrorAction SilentlyContinue).Value
    $nv = (Get-ItemProperty "$key\NonPackaged" -ErrorAction SilentlyContinue).Value
    "$hive $cap Value=$v NonPackaged=$nv" | Out-File $log -Append
    Get-ChildItem "$key\NonPackaged" -ErrorAction SilentlyContinue | ForEach-Object { "  entry $($_.PSChildName)" } | Out-File $log -Append
  }
}
Get-PnpDevice -Class Camera, Image, AudioEndpoint -ErrorAction SilentlyContinue |
  ForEach-Object { "device $($_.Class) $($_.FriendlyName) $($_.Status)" } | Out-File $log -Append
"narrator=$(Test-Path "$env:windir\System32\Narrator.exe")" | Out-File $log -Append
try {
  $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
  $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
  $bmp.Save((Join-Path $outDir 'screen.png')); "capture ok" | Out-File $log -Append
} catch { "capture failed: $_" | Out-File $log -Append }
