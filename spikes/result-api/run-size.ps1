# Publishes the 20-command sample once per Result form with NativeAOT (repository props: warnings as errors),
# records each executable's size, and runs it as the AOT exe and under the JIT.
# Usage (from any folder): pwsh spikes/result-api/run-size.ps1 [-DispatchShape PerCommand|OneSwitch]
param([ValidateSet('PerCommand', 'OneSwitch')] [string] $DispatchShape = 'PerCommand')

$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot 'Size'
$output = Join-Path $PSScriptRoot "results/size-$DispatchShape.txt"
$variants = 'Plain', 'Class', 'Struct', 'UnionStruct', 'UnionClass'

& (Join-Path $root 'generate.ps1') -DispatchShape $DispatchShape

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("# 20-command sample, SDK $(dotnet --version), $(Get-Date -Format 'yyyy-MM-dd'), default ILC settings, dispatch $DispatchShape")
$sizes = @{}
foreach ($variant in $variants)
{
    $project = Join-Path $root "$variant/Size.$variant.csproj"
    $log = dotnet publish $project -c Release --nologo -v:q -clp:NoSummary 2>&1
    if ($LASTEXITCODE -ne 0)
    {
        $log | Write-Host
        throw "publish failed: $variant"
    }

    $warnings = @($log | Select-String -Pattern ': warning ').Count
    $exe = Get-ChildItem (Join-Path $root "$variant/bin/Release") -Recurse -Filter "Size.$variant.exe" |
        Where-Object { $_.DirectoryName -like '*publish*' } | Select-Object -First 1
    $sizes[$variant] = $exe.Length
    $lines.Add("size $variant : $($exe.Length) bytes, publish warnings: $warnings")
}

foreach ($variant in $variants)
{
    $delta = $sizes[$variant] - $sizes['Plain']
    $lines.Add("delta $variant vs Plain : $delta bytes")
}

foreach ($variant in $variants)
{
    $exe = Get-ChildItem (Join-Path $root "$variant/bin/Release") -Recurse -Filter "Size.$variant.exe" |
        Where-Object { $_.DirectoryName -like '*publish*' } | Select-Object -First 1
    $lines.Add("")
    $lines.Add("## $variant, NativeAOT")
    & $exe.FullName | ForEach-Object { $lines.Add($_) }
    $lines.Add("## $variant, JIT (dotnet run -c Release)")
    dotnet run --project (Join-Path $root "$variant/Size.$variant.csproj") -c Release --nologo -v:q | ForEach-Object { $lines.Add($_) }
}

$lines | Set-Content -Path $output -Encoding utf8
$lines
