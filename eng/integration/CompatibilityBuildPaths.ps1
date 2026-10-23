#requires -Version 7.4
# Only the caller's isolated compatibility copy is changed. Ordinary projects and Debug keep real paths.
function Set-CompatibilityBuildPaths([string]$FixtureDirectory) {
  $props = Join-Path $FixtureDirectory 'Directory.Build.props'
  $source = [IO.File]::ReadAllText($props)
  $group = @'
  <PropertyGroup Condition="'$(Configuration)' == 'Release'">
    <PathMap Condition="'$(PathMap)' == ''">$(MSBuildThisFileDirectory)=/_/spar-compatibility/</PathMap>
  </PropertyGroup>

</Project>
'@
  # Keep source files, output directories, caches and rebuilds independent; normalize compiler paths only.
  if (!$source.Contains('</Project>')) { throw 'Compatibility copy has no Directory.Build.props project root.' }
  [IO.File]::WriteAllText($props, $source.Replace('</Project>', "`n" + $group))
}
