#!/usr/bin/env bash
source "$EVAL_LIB"
check package grep -q 'Include="Spar.OpenTelemetry"' Pantry/Pantry.csproj
check program grep -q 'spar.Telemetry(' Pantry/Program.cs
check builds dotnet build Pantry -c Debug
