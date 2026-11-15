# Black-box smoke test of the exact Production executable: nothing replaced, its data under artifacts/.
# Starts it minimized (it never takes focus from a visible window), waits for its window, asks it to close the way
# the shell does (WM_CLOSE), and checks the exit code and that no web engine process outlives it.
param(
    [string] $Exe = "$PSScriptRoot/../../artifacts/test-host/publish/production/SampleApp.exe",
    [int] $Runs = 3
)

$ErrorActionPreference = 'Stop'
$dataRoot = [IO.Path]::GetFullPath("$PSScriptRoot/../../artifacts/webview2-profiles/test-host-blackbox")

for ($run = 1; $run -le $Runs; $run++) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $start = New-Object Diagnostics.ProcessStartInfo $Exe
    $start.ArgumentList.Add('--spar-data-root')
    $start.ArgumentList.Add($dataRoot)
    $start.UseShellExecute = $true
    $start.WindowStyle = 'Minimized'
    $process = [Diagnostics.Process]::Start($start)

    while ($process.MainWindowHandle -eq 0 -and $clock.ElapsedMilliseconds -lt 15000) {
        Start-Sleep -Milliseconds 20
        $process.Refresh()
    }
    $window = $clock.ElapsedMilliseconds
    $title = $process.MainWindowTitle

    # Give the page time to load before closing, as a user would.
    Start-Sleep -Milliseconds 1500
    $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $($process.Id)" | Select-Object -ExpandProperty ProcessId)
    $closing = $clock.ElapsedMilliseconds
    [void] $process.CloseMainWindow()
    $exited = $process.WaitForExit(15000)
    $closed = $clock.ElapsedMilliseconds - $closing

    Start-Sleep -Milliseconds 500
    $left = @($children | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
    "SPIKE blackbox run=$run window-after=${window}ms title='$title' engine-children=$($children.Count) exited=$exited code=$($process.ExitCode) close-to-exit=${closed}ms children-left=$($left.Count)"
}

Remove-Item -Recurse -Force $dataRoot -ErrorAction SilentlyContinue
