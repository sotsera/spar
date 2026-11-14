# Summarises the ILC map files of the size apps (publish with -p:IlcGenerateMapFile=true first): bytes of nodes
# whose symbol mentions Spar's Result, and how much of their code is the canonical instantiation shared by reference-type T.
$ErrorActionPreference = 'Stop'
$variants = 'Class', 'Struct', 'UnionStruct', 'UnionClass'
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# Nodes whose name mentions Spar_Result_ (ILC map, Release, dispatch PerCommand)')
$lines.Add('variant | all nodes | Result nodes | Result code (MethodCode) | canonical Result<__Canon, __Canon> code (shared by reference-type T) | code for the 6 value-type T | Result code nodes')
foreach ($variant in $variants)
{
    $path = Join-Path $PSScriptRoot "$variant/obj/Release/net11.0/win-x64/native/Size.$variant.map.xml"
    [xml] $map = Get-Content $path -Raw
    $nodes = $map.ObjectNodes.ChildNodes | Where-Object { $_.Length }
    $all = ($nodes | Measure-Object -Property Length -Sum).Sum
    $result = $nodes | Where-Object { $_.Name -like '*Spar_Result_*' }
    $resultBytes = ($result | Measure-Object -Property Length -Sum).Sum
    $code = $result | Where-Object { $_.LocalName -eq 'MethodCode' }
    $codeBytes = ($code | Measure-Object -Property Length -Sum).Sum
    $canonical = $code | Where-Object {
        $_.Name -like '*Result_2<System___Canon__System___Canon>*' -or $_.Name -like '*Result_1<System___Canon>*'
    }
    $shared = ($canonical | Measure-Object -Property Length -Sum).Sum
    $lines.Add("$variant | $all | $resultBytes | $codeBytes | $shared | $($codeBytes - $shared) | $(@($code).Count)")
}

$kinds = foreach ($variant in 'Class', 'UnionStruct')
{
    $path = Join-Path $PSScriptRoot "$variant/obj/Release/net11.0/win-x64/native/Size.$variant.map.xml"
    [xml] $map = Get-Content $path -Raw
    $map.ObjectNodes.ChildNodes | Where-Object { $_.Length -and $_.Name -like '*Spar_Result_*' } |
        Group-Object LocalName | ForEach-Object {
            [pscustomobject]@{ Variant = $variant; Kind = $_.Name; Count = $_.Count; Bytes = ($_.Group | Measure-Object -Property Length -Sum).Sum }
        }
}

$lines.Add('')
$lines.Add('# Result nodes by kind, Class vs UnionStruct')
$kinds | Sort-Object Kind, Variant | ForEach-Object { $lines.Add("$($_.Kind) | $($_.Variant) | $($_.Count) nodes | $($_.Bytes) bytes") }

$output = Join-Path $PSScriptRoot '../results/map-summary.txt'
$lines | Set-Content -Path $output -Encoding utf8
$lines
