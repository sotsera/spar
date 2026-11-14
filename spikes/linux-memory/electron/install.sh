#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/linux-memory): bash electron/install.sh
# Installs the pinned Electron into electron/node_modules with install scripts disabled, then fetches Electron's
# binary explicitly by running the package's own install.js (it downloads the release zip from GitHub into
# $electron_config_cache and extracts it to node_modules/electron/dist). All npm and Electron state stays in $SPAR_WORK.
set -eu
source wsl/env.sh
cd electron
npm install --ignore-scripts --no-audit --no-fund
node node_modules/electron/install.js
du -sh node_modules "$electron_config_cache" | sed 's/^/SIZE /'
ls node_modules/electron/dist | head -40
# Electron links NSS and NSPR, which this WSL distribution does not have. Without root, fetch the distribution's own
# packages with apt-get download (no install) and extract them into the working folder; start.sh adds them to
# LD_LIBRARY_PATH for Electron only.
libs=$SPAR_WORK/electron-libs
mkdir -p "$libs/debs" "$libs/root"
(cd "$libs/debs" && apt-get download libnss3 libnspr4 && for f in *.deb; do dpkg-deb -x "$f" ../root; done)
