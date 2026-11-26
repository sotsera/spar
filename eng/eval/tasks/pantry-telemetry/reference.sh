#!/usr/bin/env bash
source "$EVAL_LIB"
replace Pantry/Pantry.csproj '</Project>' '  <ItemGroup>
    <PackageReference Include="Spar.OpenTelemetry" />
  </ItemGroup>
</Project>'
replace Pantry/Program.cs '        .Size(900, 600));' '        .Size(900, 600));

    spar.Telemetry();'
