#!/usr/bin/env bash
# Prepares a Linux run of the CI lane in WSL (decision "Linux runs in WSL for M1a"): a fresh clone of the repository's
# HEAD, the .NET SDK, the caches and Chromium's missing libraries, all in the distribution's /var/tmp/spar-linux (WSL
# empties /tmp when the distribution restarts). HOME and every XDG folder are under the clone's own
# artifacts/linux-homes/<run>/, on a Linux filesystem, because XDG_RUNTIME_DIR must be 0700, which a Windows drive
# cannot give. Nothing is written to the user's home, and nothing needs root.
#
# From Git Bash on Windows (MSYS_NO_PATHCONV keeps the /mnt paths as they are):
#   MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/eng/wsl/setup.sh /mnt/<repo> <run>
# then each step with step.sh. Commit first: the clone has only what HEAD has.
set -euo pipefail
source_repo=$1
run=$2
base=/var/tmp/spar-linux
cache=$base/cache
work=$base/$run
homes=$work/repo/artifacts/linux-homes/$run

mkdir -p "$cache" "$work"
if [ ! -d "$work/repo" ]; then
  git -c safe.directory='*' clone --quiet --no-hardlinks "$source_repo" "$work/repo"
fi

mkdir -p "$homes"/{home,cache,config,data,state,runtime}
chmod 700 "$homes/runtime"
export HOME=$homes/home XDG_CACHE_HOME=$homes/cache XDG_CONFIG_HOME=$homes/config XDG_DATA_HOME=$homes/data
export XDG_STATE_HOME=$homes/state XDG_RUNTIME_DIR=$homes/runtime

# The SDK global.json asks for, once per machine.
sdk=$(sed -n 's/.*"version": *"\([^"]*\)".*/\1/p' "$work/repo/global.json" | head -1)
if [ ! -x "$cache/dotnet/dotnet" ] || ! "$cache/dotnet/dotnet" --list-sdks | grep -q "^$sdk "; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$cache/dotnet-install.sh"
  bash "$cache/dotnet-install.sh" --version "$sdk" --install-dir "$cache/dotnet" --no-path
fi

# Playwright's headless Chromium needs NSS and NSPR, which a minimal Ubuntu lacks: downloaded and unpacked, no root.
if [ ! -e "$cache/libs/usr/lib/x86_64-linux-gnu/libnss3.so" ]; then
  mkdir -p "$cache/libs/debs"
  (cd "$cache/libs/debs" && apt-get download libnss3 libnspr4 && for deb in *.deb; do dpkg -x "$deb" "$cache/libs"; done)
fi

echo "clone at $(git -C "$work/repo" log --oneline -1)"
"$cache/dotnet/dotnet" --list-sdks
