#!/usr/bin/env bash
# M1c's exit in WSL (Ubuntu-24.04, the normal user, no root): the budgets in Release on both test lanes, the Notebook's
# Linux executable, and the start-up refusal of an old WebKitGTK; docs/evidence/m1c-exit.md. From Git Bash on Windows,
# at the repository's root (MSYS_NO_PATHCONV keeps the path):
#   MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1c-exit/wsl.sh <step>...
# Steps, in this order: sync budgets launch budgetsaot notebook minimum (launch once or more).
# It works in ../m1c-blackbox/wsl.sh's copy of the tree (/var/tmp/spar-m1c-blackbox/repo), whose `sync` step it runs,
# with the same environment: the Xvfb unpacked by ../m1c-host/wsl.sh into /var/tmp/spar-m1c-host/tools (not installed),
# each test process its own Xvfb on abstract sockets, DISPLAY and WAYLAND_DISPLAY unset, so nothing opens on WSLg's
# display. Nothing here touches another run's processes.
set -uo pipefail
# No core dumps unless SPAR_CORE_DUMPS=1 (decision "Linux test runs write no core dumps"); children inherit it.
[ "${SPAR_CORE_DUMPS:-}" = 1 ] || prlimit --core=1 --pid $$
source_repo=$(cd "$(dirname "$0")/../../.." && pwd)
base=/var/tmp/spar-m1c-blackbox
repo=$base/repo
tools=/var/tmp/spar-m1c-host/tools
cache=/var/tmp/spar-linux/cache
homes=$repo/artifacts/linux-homes/m1c-exit
logs=$source_repo/artifacts/linux-runs/m1c-exit
results=$repo/artifacts/test-results
dev_probe=$repo/tests/apps/BridgeProbe/bin/Development/Debug/net11.0/linux-x64/BridgeProbe
mkdir -p "$base/bin" "$logs"

export DOTNET_ROOT=$cache/dotnet PATH=$base/bin:$cache/dotnet:$PATH
export NUGET_PACKAGES=$cache/nuget-packages NUGET_HTTP_CACHE_PATH=$cache/nuget-http npm_config_cache=$cache/npm
export PLAYWRIGHT_BROWSERS_PATH=$cache/ms-playwright
export LD_LIBRARY_PATH=$cache/libs/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
unset DISPLAY WAYLAND_DISPLAY WEBKIT_INSPECTOR_SERVER WEBKIT_INSPECTOR_HTTP_SERVER WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS
unset GSK_RENDERER WEBKIT_DISABLE_DMABUF_RENDERER WEBKIT_DISABLE_COMPOSITING_MODE __NV_DISABLE_EXPLICIT_SYNC GTK_DEBUG
unset GTK_A11Y NO_AT_BRIDGE AT_SPI_BUS_ADDRESS XDG_CURRENT_DESKTOP DESKTOP_STARTUP_ID XDG_ACTIVATION_TOKEN SPAR_AUTOMATED
unset PULSE_SERVER BROWSER SPAR_TEST_DISPLAY CI DBUS_SESSION_BUS_ADDRESS
unset SPAR_LINUX_HOST_APP SPAR_LINUX_WASM_APP SPAR_LINUX_NOTEBOOK SPAR_LINUX_FLOOD SPAR_LINUX_NOTEBOOK_LABEL
for v in $(env | grep -o '^\(JSC_\|SPAR_DEV_\|BRIDGE_PROBE_\)[^=]*' || true); do unset "$v"; done

# The Xvfb wrapper of ../m1c-blackbox/wsl.sh: the unpacked server with xkbcomp where it looks for it, in a bubblewrap
# mount namespace that ends with its parent.
cat > "$base/bin/Xvfb" <<EOF
#!/bin/sh
export LD_LIBRARY_PATH=$tools/root/usr/lib/x86_64-linux-gnu
exec bwrap --die-with-parent --dev-bind / / --bind /usr/bin "$tools/realbin" --bind "$tools/usrbin" /usr/bin -- "$tools/root/usr/bin/Xvfb" "\$@"
EOF
chmod +x "$base/bin/Xvfb"

mkdir -p "$homes"/{home,cache,config,data,state} /dev/shm/spar-m1c-exit
chmod 700 /dev/shm/spar-m1c-exit
export HOME=$homes/home XDG_CACHE_HOME=$homes/cache XDG_CONFIG_HOME=$homes/config XDG_DATA_HOME=$homes/data
export XDG_STATE_HOME=$homes/state XDG_RUNTIME_DIR=/dev/shm/spar-m1c-exit

timed() {
  local start=$(date +%s%N)
  "$@"
  local exit=$?
  echo "STEP exit $exit in $(( ($(date +%s%N) - start) / 1000000 )) ms: $*"
  return $exit
}

# The processes the tests left, by their runtime folder (the window tests' and the black box's) and this copy's HOME.
census() {
  local left=0
  for p in /proc/[0-9]*; do
    local environ
    environ=$( { tr '\0' '\n' < "$p/environ"; } 2>/dev/null) || continue
    grep -q "^XDG_RUNTIME_DIR=/dev/shm/spar-\(test\|bb\)-" <<<"$environ" || continue
    grep -q "^HOME=$repo/" <<<"$environ" || continue
    echo "left: $(basename "$p") $(tr '\0' ' ' < "$p/cmdline" | cut -c1-120)"; left=$((left + 1))
  done
  echo "processes left by the tests: $left"
}

# Copies a file the tests recorded into this run's logs, under a name of its own.
keep() {
  [ -f "$1" ] && cp "$1" "$logs/$2" && echo "kept $2" && cat "$1"
}

step() {
  case $1 in
    sync) bash "$source_repo/docs/evidence/m1c-blackbox/wsl.sh" sync client ;;
    budgets)
      # The M1b exit's method (docs/evidence/m1b-budgets.md): BudgetTests in Release on the CoreCLR test lane.
      rm -f "$results/Spar.WindowTests/budgets.txt"
      (cd "$repo" && timed dotnet test --project tests/Spar.WindowTests -c Release --filter-class "Spar.WindowTests.BudgetTests")
      keep "$results/Spar.WindowTests/budgets.txt" budgets-coreclr.txt
      census ;;
    launch)
      # The Production launch alone, in a test process that never loads WebKit itself: PSS divides a shared page among
      # every process that maps it, so in the whole class the test process's own windows (and their web processes) take
      # a share of WebKit's and GTK's libraries from the app's.
      rm -f "$results/Spar.WindowTests/budgets.txt"
      (cd "$repo" && timed dotnet test --project tests/Spar.WindowTests -c Release -- \
        --filter-method "*The_Production_app_starts_to_ui_ready_and_holds_its_working_set")
      keep "$results/Spar.WindowTests/budgets.txt" "budgets-launch-$(date +%H%M%S).txt"
      census ;;
    budgetsaot)
      # The same tests on the NativeAOT test lane.
      rm -f "$results/Spar.WindowTests/budgets.txt"
      (cd "$repo" && timed dotnet publish tests/Spar.WindowTests -c Release -p:SparTestLane=Aot -o artifacts/aot/Spar.WindowTests) || return 1
      (cd "$repo" && timed artifacts/aot/Spar.WindowTests/Spar.WindowTests -class "Spar.WindowTests.BudgetTests")
      keep "$results/Spar.WindowTests/budgets.txt" budgets-aot.txt
      census ;;
    notebook)
      # The Notebook's Production executable for linux-x64: published, its folder listed, then started to ui-ready under
      # the black box's private Xvfb twelve times (one discarded), with its close and its PSS.
      rm -rf "$base/notebook-exit"
      (cd "$repo" && timed dotnet publish samples/Notebook -c Release -r linux-x64 -o "$base/notebook-exit") || return 1
      (cd "$base/notebook-exit" && ls -l && stat -c '%n %s B' Notebook && file Notebook)
      # The executable alone in a folder of its own (without its .dbg and the libraries' .pdb files), as it ships.
      rm -rf "$base/notebook-alone" && mkdir -p "$base/notebook-alone" && cp "$base/notebook-exit/Notebook" "$base/notebook-alone/"
      ls -l "$base/notebook-alone"
      export SPAR_LINUX_NOTEBOOK=$base/notebook-alone/Notebook SPAR_LINUX_NOTEBOOK_LABEL=exit
      (cd "$repo" && timed dotnet test --project tests/Spar.WindowTests -c Release -- \
        --filter-method "*The_Notebook_starts_and_closes_and_its_numbers_are_recorded" --output Detailed)
      keep "$results/Spar.WindowTests/linux-host-exit.txt" notebook.txt
      census ;;
    minimum)
      # The start-up refusal of an old WebKitGTK: the decision's unit test with the real minimum, then the real-host test
      # on the probe's Development build, which raises the minimum above the installed WebKitGTK.
      (cd "$repo" && timed dotnet build tests/apps/BridgeProbe -c Debug -p:SparDevServer=false) || return 1
      (cd "$repo" && timed dotnet test --project tests/Spar.Tests -c Release -- \
        --filter-method "*WebKitGTK_2_48_is_the_oldest_supported" --output Detailed)
      export SPAR_LINUX_HOST_APP=$dev_probe
      (cd "$repo" && timed dotnet test --project tests/Spar.WindowTests -c Release -- \
        --filter-method "*A_WebKitGTK_older_than_required_is_exit_4_with_a_message" --output Detailed)
      keep "$results/Spar.WindowTests/linux-startup.txt" minimum-startup.txt
      census ;;
    *) echo "unknown step $1"; return 2 ;;
  esac
}

status=0
for name in "$@"; do
  { echo "STEP $name on $(uname -r), dotnet $(dotnet --version 2>/dev/null), WebKitGTK $(pkg-config --modversion webkitgtk-6.0 2>/dev/null)"; step "$name"; } 2>&1 | tee "$logs/$name.log"
  [ "${PIPESTATUS[0]}" -eq 0 ] || status=1
done
exit $status
