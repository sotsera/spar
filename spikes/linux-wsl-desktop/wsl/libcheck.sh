#!/usr/bin/env bash
# Usage (inside WSL, in the working copy's spikes/linux-wsl-desktop): bash wsl/libcheck.sh
# Starts the AOT executable with one native library hidden, without changing the system: bwrap gives the process a
# private mount namespace in which the library file is replaced by /dev/null (dlopen then fails), or a helper folder by
# an empty tmpfs. Also tries an invalid library file placed next to a copy of the executable.
set -u
lib=/usr/lib/x86_64-linux-gnu
g3=Wsl.Linux/bin/Release/net11.0/linux-x64/publish/Wsl.Linux
g4=Wsl.Linux/bin/gtk4/Release/net11.0/linux-x64/publish/Wsl.Linux
art=../../artifacts/linux-wsl-desktop/libcheck
mkdir -p "$art"
export XDG_CACHE_HOME=$(cd "$art" && pwd)/cache
hide() { # <name> <exe> <bwrap args...> -- <app args...>
  local name=$1 exe=$2; shift 2
  local b=(); while [ "$1" != -- ]; do b+=("$1"); shift; done; shift
  echo "=== $name"
  local start=$(date +%s%N)
  timeout 30 bwrap --dev-bind / / "${b[@]}" -- "$exe" --label "libcheck-$name" --order load-first --after-ready 200 "$@" >"$art/$name.log" 2>&1
  local code=$?
  echo "exit=$code after $(( ($(date +%s%N) - start) / 1000000 )) ms"
  grep -v -E '^\[ *[0-9.]+ ms\] (serve|page:|window-facts|portal|start choice|gtk-init|view-|navigate|visible|window|load-|theme)|^$|libEGL|MESA' "$art/$name.log" | head -14
}
real() { readlink -f "$1"; }
hide none "$g3" -- 
hide gtk3-webkit "$g3" --ro-bind /dev/null "$(real $lib/libwebkit2gtk-4.1.so.0)" --
hide gtk3-webkit-nocheck "$g3" --ro-bind /dev/null "$(real $lib/libwebkit2gtk-4.1.so.0)" -- --no-lib-check
hide gtk3-jsc "$g3" --ro-bind /dev/null "$(real $lib/libjavascriptcoregtk-4.1.so.0)" --
hide gtk3-soup "$g3" --ro-bind /dev/null "$(real $lib/libsoup-3.0.so.0)" --
hide gtk4-webkit "$g4" --ro-bind /dev/null "$(real $lib/libwebkitgtk-6.0.so.4)" --
hide gtk4-gtk "$g4" --ro-bind /dev/null "$(real $lib/libgtk-4.so.1)" --
hide gtk3-helpers "$g3" --tmpfs "$lib/webkit2gtk-4.1" --
hide gtk4-helpers "$g4" --tmpfs "$lib/webkitgtk-6.0" --
hide gtk4-bwrap-missing "$g4" --ro-bind /dev/null /usr/bin/bwrap --
# An invalid file with the library's name next to a private copy of the executable (the loader probes the app folder first).
copy=$art/appdir
rm -rf "$copy"; mkdir -p "$copy"; cp "$g3" "$copy/"; : >"$copy/libwebkit2gtk-4.1.so.0"
hide gtk3-appdir-empty-file "$copy/Wsl.Linux" --
