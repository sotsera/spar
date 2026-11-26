#!/usr/bin/env bash
set -euo pipefail
dotnet publish Pantry -c Release -r linux-x64
stat -c %s "$(find Pantry/bin -type f -name Pantry -path '*linux-x64*' -path '*publish*' | head -1)" > publish-size.txt
