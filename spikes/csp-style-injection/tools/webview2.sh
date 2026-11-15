#!/usr/bin/env bash
# Usage: tools/webview2.sh <label> <profile> <page> [dist]
# Runs spikes/csp-navigation's WebView2 host offscreen (never activated) on this spike's folders, with its log in
# artifacts/csp-style-injection/webview2/<label>.log. The page origin is the host's https://app.spar.invalid.
set -u
export MSYS_NO_PATHCONV=1
here=$(cygpath -w "$(cd "$(dirname "$0")/.." && pwd)" | tr '\' /)
label=$1; profile=$2; page=$3; dist=${4:-dist}
out="$here/../../artifacts/csp-style-injection/webview2"
mkdir -p "$out"
exe="$here/../csp-navigation/Csp.Windows/bin/Release/net11.0/win-x64/publish/Csp.Windows.exe"
"$exe" --offscreen --label "$label" --profile "$profile" --page "$page" \
  --root "$here/frontend/obj/$dist" --root "$here/pages" --root "$here/../csp-navigation/pages" >"$out/$label.log" 2>&1
echo "exit=$? log=artifacts/csp-style-injection/webview2/$label.log"
