#!/usr/bin/env bash
# Publishes the WebView2 host once per binding (NativeAOT) and prints publish time, file sizes and every warning.
set -u
cd "$(dirname "$0")"
for binding in Com WinRT; do
  out="WebView2.App/bin/publish/$binding"
  start=$(date +%s.%N)
  dotnet publish WebView2.App -c Release -p:SpikeWebView2=$binding -o "$out" > "WebView2.App/obj/publish-$binding.log" 2>&1
  status=$?
  end=$(date +%s.%N)
  echo "$binding exit=$status seconds=$(awk "BEGIN{printf \"%.1f\", $end - $start}")"
  ls -l "$out" | awk 'NR>1 && $NF !~ /\.pdb$/ {printf "    %10d %s\n", $5, $NF}'
  grep -E "warning|error" "WebView2.App/obj/publish-$binding.log" | sed 's/^ *//' | sort -u | sed 's/^/    /'
done
