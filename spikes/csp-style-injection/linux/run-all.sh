#!/usr/bin/env bash
# Usage (in the container as uid 1000): bash /src/spikes/csp-style-injection/linux/run-all.sh
# Copies spikes/csp-navigation's WebKitGTK host and this spike's pages and builds into /work, publishes the host for
# WebKitGTK 6.0 with NativeAOT, and runs the five scenarios on Xvfb :99. Logs go to /out (artifacts/csp-style-injection/
# webkitgtk); WebKit's data and HOME stay under /home/run (artifacts/linux-homes/csp-style-injection).
set -eu
mkdir -p /work/spikes/csp-navigation /work/spikes/csp-style-injection /out "$HOME"
cp /src/global.json /src/Directory.Build.props /src/Directory.Packages.props /src/.editorconfig /work/
(cd /src/spikes/csp-navigation && tar cf - --exclude=bin --exclude=obj Directory.Build.props Directory.Packages.props Csp.Shared Csp.Linux pages) \
  | (cd /work/spikes/csp-navigation && tar xf -)
(cd /src/spikes/csp-style-injection && tar cf - pages frontend/obj/dist frontend/obj/dist-cssom) | (cd /work/spikes/csp-style-injection && tar xf -)

cd /work/spikes/csp-navigation
start=$(date +%s)
dotnet publish Csp.Linux/Csp.Linux.csproj -c Release -p:SparGtk=4 -nologo -clp:ErrorsOnly\;Summary 2>&1 | tail -3
host=Csp.Linux/bin/gtk4/Release/net11.0/linux-x64/publish/Csp.Linux
echo "SPAR_PUBLISH $(( $(date +%s) - start ))s $(stat -c %s $host) bytes"

here=/work/spikes/csp-style-injection
run() {
  label=$1; profile=$2; page=$3; dist=${4:-dist}
  timeout 60 "$host" --label "$label" --profile "$profile" --page "$page" \
    --root "$here/frontend/obj/$dist" --root "$here/pages" --root /work/spikes/csp-navigation/pages \
    --data-dir "$HOME/webkit-$label" >"/out/$label.log" 2>&1
  echo "exit=$? $label"
}

run probes-strict strict /probes.html
run probes-nonce nonce /probes.html
run radix-strict strict / dist
run radix-nonce nonce / dist
run cssom-strict strict / dist-cssom
pkg-config --modversion webkitgtk-6.0 > /out/webkitgtk-version.txt 2>&1 || dpkg -s libwebkitgtk-6.0-4 | grep Version > /out/webkitgtk-version.txt
