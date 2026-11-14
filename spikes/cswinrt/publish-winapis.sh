#!/usr/bin/env bash
# Publishes one NativeAOT exe per Windows API probe and prints size, publish time and every warning.
set -u
cd "$(dirname "$0")"
for variant in NoWinRT Baseline Vault Picker Toast All; do
  args=(-c Release -o "WinApis.App/bin/publish/$variant")
  if [ "$variant" = NoWinRT ]; then args+=(-p:SpikeNoWinRT=true -p:SpikeApi=Picker); else args+=(-p:SpikeApi=$variant); fi
  start=$(date +%s.%N)
  dotnet publish WinApis.App "${args[@]}" > "WinApis.App/obj/publish-$variant.log" 2>&1
  status=$?
  end=$(date +%s.%N)
  size=$(stat -c %s "WinApis.App/bin/publish/$variant/WinApis.App.exe" 2>/dev/null || echo missing)
  echo "$variant exit=$status seconds=$(awk "BEGIN{printf \"%.1f\", $end - $start}") exe=$size files=$(ls "WinApis.App/bin/publish/$variant" | grep -v '\.pdb$' | tr '\n' ' ')"
  grep -E "warning|error" "WinApis.App/obj/publish-$variant.log" | sed 's/^ *//' | sort -u | sed 's/^/    /'
done
