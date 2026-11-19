# Attributes the NativeAOT image of each variant to assemblies and types, from the ILC map files (the project sets
# IlcGenerateMapFile). Each map node's mangled name starts with its owning assembly (dots as underscores; System.Private.*
# as S_P_*), after a node-kind prefix this script strips. Writes results/map-assemblies.txt, results/map-types.txt and
# results/map-stj-types.txt.
$ErrorActionPreference = 'Stop'
$variants = 0, 1, 2, 3, 4
$native = { param($v) Join-Path $PSScriptRoot "ConfigCost/obj/v$v/Production/Release/net11.0/win-x64/native" }

# The assemblies ILC saw, longest mangled prefix first.
$assemblies = foreach ($v in $variants)
{
    $rsp = Join-Path (& $native $v) 'ConfigCost.ilc.rsp'
    Get-Content $rsp | Where-Object { $_ -match '\.dll$' } | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension(($_ -replace '^-r:', '')) }
}
$prefixes = $assemblies | Sort-Object -Unique | ForEach-Object {
    $mangled = if ($_ -like 'System.Private.*') { 'S_P_' + $_.Substring(15).Replace('.', '_') } else { $_.Replace('.', '_') }
    [pscustomobject]@{ Assembly = $_; Prefix = $mangled + '_' }
} | Sort-Object { $_.Prefix.Length } -Descending

$strip = '^(\?\?_7|\?__(NONGCSTATICS|GCSTATICS|THREADSTATICS)@|__GenericDict_|__writableData|__readonlydata_|__ehinfo_|' +
    '__GetNonGCStaticBase_|__GetGCStaticBase_|__GetThreadStaticBase_|__InterfaceDispatchMap_|__NEW_|__RuntimeFieldHandle_|' +
    '__RuntimeMethodHandle_|__GVMSlots_|__SealedVTable_|__DelegateCtor_|__fatpointer_|__unbox_|__VirtualCall_|__TypeHandle_|' +
    '__GenericLookup_|__InterfaceDispatchCell_|_unbox_|__optionalfields_|__dictionaryLayout_)'

function Attribute([string] $name)
{
    $n = $name -replace $strip, ''
    $n = $n -replace '@@6B@$', '' -replace '@@$', ''
    foreach ($p in $prefixes)
    {
        if ($n.StartsWith($p.Prefix, [StringComparison]::Ordinal))
        {
            $rest = $n.Substring($p.Prefix.Length)
            $type = ($rest -split '__', 2)[0] -replace '<.*$', ''
            return $p.Assembly, $type
        }
    }
    return $null, $null
}

$byVariant = @{}
$types = @{}
$allTypes = @{}
foreach ($v in $variants)
{
    [xml] $map = Get-Content (Join-Path (& $native $v) 'ConfigCost.map.xml') -Raw
    $sums = @{}
    $typeSums = @{}
    $allSums = @{}
    foreach ($node in $map.ObjectNodes.ChildNodes)
    {
        if (-not $node.Length) { continue }
        $length = [long] $node.Length
        $assembly, $type = Attribute $node.Name
        $key = if ($assembly) { $assembly } else { "(unattributed $($node.LocalName))" }
        $sums[$key] = $sums[$key] + $length
        $sums['(all nodes)'] = $sums['(all nodes)'] + $length
        if ($assembly -eq 'System.Text.Json')
        {
            $tkey = "$type | $($node.LocalName)"
            $typeSums[$tkey] = $typeSums[$tkey] + $length
        }
        $akey = if ($assembly) { "$assembly | $type" } else { "(unattributed $($node.LocalName)) |" }
        $allSums[$akey] = $allSums[$akey] + $length
    }
    $byVariant[$v] = $sums
    $types[$v] = $typeSums
    $allTypes[$v] = $allSums
}

# Every type whose bytes changed by 1 KB or more from v0 to v1, or from v1 to v2.
$a = [Collections.Generic.List[string]]::new()
$a.Add('# Types whose map nodes changed by >= 1,024 B: assembly | type | v0 | v1 | v2 | v4 | v1-v0 | v2-v1 | v4-v0')
$akeys = $allTypes[0].Keys + $allTypes[1].Keys + $allTypes[2].Keys | Sort-Object -Unique
$arows = foreach ($k in $akeys)
{
    $x = [long] $allTypes[0][$k]; $y = [long] $allTypes[1][$k]; $z = [long] $allTypes[2][$k]; $w = [long] $allTypes[4][$k]
    if ([math]::Abs($y - $x) -ge 1024 -or [math]::Abs($z - $y) -ge 1024)
    {
        [pscustomobject]@{ Key = $k; X = $x; Y = $y; Z = $z; W = $w; D1 = $y - $x; D2 = $z - $y; D4 = $w - $x }
    }
}
foreach ($row in $arows | Sort-Object { $_.D1 + $_.D2 } -Descending)
{
    $a.Add("$($row.Key) | $($row.X) | $($row.Y) | $($row.Z) | $($row.W) | $($row.D1) | $($row.D2) | $($row.D4)")
}
$a | Set-Content (Join-Path $PSScriptRoot 'results/map-types.txt') -Encoding utf8

$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Bytes of ILC map nodes by owning assembly (MethodCode, GCInfo, EETypes, statics, dictionaries...), Production NativeAOT win-x64')
$lines.Add('# "unattributed" nodes are image-wide (embedded metadata, frozen objects, stack-trace map, hashtables).')
$lines.Add('key | v0 | v1 | v2 | v3 | v4 | v1-v0 | v2-v0 | v3-v0 | v4-v0')
$keys = $variants | ForEach-Object { $byVariant[$_].Keys } | Sort-Object -Unique
$rows = foreach ($k in $keys)
{
    $vals = $variants | ForEach-Object { [long] $byVariant[$_][$k] }
    [pscustomobject]@{ Key = $k; V = $vals; Delta = $vals[2] - $vals[0] }
}
foreach ($row in $rows | Sort-Object { [math]::Abs($_.Delta) }, { $_.V[3] } -Descending)
{
    $v = $row.V
    if ($v[0] -eq $v[1] -and $v[1] -eq $v[2] -and $v[2] -eq $v[3] -and $v[3] -eq $v[4] -and $v[0] -lt 20000) { continue }
    $lines.Add("$($row.Key) | $($v[0]) | $($v[1]) | $($v[2]) | $($v[3]) | $($v[4]) | $($v[1] - $v[0]) | $($v[2] - $v[0]) | $($v[3] - $v[0]) | $($v[4] - $v[0])")
}
$lines | Set-Content (Join-Path $PSScriptRoot 'results/map-assemblies.txt') -Encoding utf8

$t = [Collections.Generic.List[string]]::new()
$t.Add('# System.Text.Json map nodes by type and node kind: v0 (baseline) against v1 (JSON configuration), v2 and v4 (AddJsonStream)')
$t.Add('type | node kind | v0 | v1 | v2 | v4 | v1-v0')
$tkeys = $types[0].Keys + $types[1].Keys + $types[2].Keys + $types[4].Keys | Sort-Object -Unique
$trows = foreach ($k in $tkeys)
{
    $a = [long] $types[0][$k]; $b = [long] $types[1][$k]; $c = [long] $types[2][$k]; $e = [long] $types[4][$k]
    [pscustomobject]@{ Key = $k; A = $a; B = $b; C = $c; E = $e; D = $b - $a }
}
foreach ($row in $trows | Sort-Object { $_.D }, { $_.B } -Descending)
{
    $t.Add("$($row.Key) | $($row.A) | $($row.B) | $($row.C) | $($row.E) | $($row.D)")
}
$t | Set-Content (Join-Path $PSScriptRoot 'results/map-stj-types.txt') -Encoding utf8
$lines
