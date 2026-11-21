#!/usr/bin/env bash
# The Linux window tests in Spar's CI image (eng/linux-ci/Dockerfile), the steps the workflow's linux-window job runs
# (decision "The Linux CI image and job; online and offline on Linux"). The repository is mounted read-only at /src and
# copied to the container user's own ~/repo, so a run never writes into the mounted tree (locally, the Windows working
# copy) and the build runs on the container's filesystem; results stay in ~/repo/artifacts/test-results/, from which
# the workflow (or a local run) copies them out with `docker cp`.
#
# The container, as the workflow starts it (uid 1000 from the image; --init reaps the processes the tests leave to it;
# seccomp=unconfined because Docker's default profile refuses the user namespaces of WebKit's bwrap sandbox and of
# network-switch.sh; on a host with AppArmor, such as GitHub's runners, the profile apparmor-profile beside this file,
# loaded with `sudo apparmor_parser -r`, because docker-default denies the mounts bwrap makes in its namespaces; a host
# without AppArmor, such as Docker Desktop's VM, leaves that option out; systempaths=unconfined because the kernel lets
# bwrap mount a new /proc only where the container's /proc is not masked):
#   docker run -d --name spar-linux-ci --init --shm-size=1g --security-opt seccomp=unconfined \
#     --security-opt apparmor=spar-linux-ci --security-opt systempaths=unconfined \
#     -e CI=true -v "$PWD:/src:ro" spar-linux-ci sleep infinity
#   docker exec spar-linux-ci bash /src/eng/linux-ci/run.sh copy client build window windowaot apps blackbox blackboxdev
#   docker cp spar-linux-ci:/home/spar/repo/artifacts/test-results artifacts/
#   docker rm -f spar-linux-ci
#
# Steps:
#   copy       the tree from /src without .git, bin, obj, node_modules, artifacts and spikes
#   client     the TypeScript SDK, compiled (the apps' builds take it from obj/dist)
#   build      Spar.WindowTests and Notebook.EndToEndTests, Debug
#   check      the web process's sandbox and the network switch, before any test (a clear message instead of a skip)
#   window     the window tests on CoreCLR (trait Surface=Window), under the network switch
#   windowaot  the same published with NativeAOT, each executable run under the network switch
#   apps       the BridgeProbe tests' other executables: the probe's Development build, its WebAssembly build, the Notebook
#   blackbox   the black-box tests (trait Surface=BlackBox) on CoreCLR and the Production probe, without Probe=Development,
#              OS=Windows, Kind=Measurement and Needs=WritableProcSys; a skipped test fails this step
#   blackboxdev  the tests that need the probe's Development build (Probe=Development) on it; no skips
set -uo pipefail
# No core dumps unless SPAR_CORE_DUMPS=1 (decision "Linux test runs write no core dumps"): WSL and Docker Desktop pipe a
# crashed process's core to Windows' %TEMP%\wsl-crashes, about 6 GB for each of our apps, and a piped core_pattern
# ignores a limit of 0; a limit of exactly 1 byte turns it off. Children inherit it.
[ "${SPAR_CORE_DUMPS:-}" = 1 ] || prlimit --core=1 --pid $$
repo=$HOME/repo
switch=(sh "$repo/eng/linux-ci/network-switch.sh")
apps=$HOME/apps
dev_probe=$repo/tests/apps/BridgeProbe/bin/Development/Debug/net11.0/linux-x64/BridgeProbe
export SPAR_TEST_RESULTS=$repo/artifacts/test-results
export ContinuousIntegrationBuild=${ContinuousIntegrationBuild:-true}
# Nothing from a desktop session reaches the tests: each test process starts its own Xvfb and D-Bus session.
unset DISPLAY WAYLAND_DISPLAY DBUS_SESSION_BUS_ADDRESS XDG_RUNTIME_DIR

timed() {
  local start
  start=$(date +%s%N)
  "$@"
  local status=$?
  echo "STEP exit $status in $(( ($(date +%s%N) - start) / 1000000 )) ms: $*"
  return $status
}

# A run whose summary shows no test, or (with --no-skips) a skipped one, fails: an empty selection is not a pass
# (fixture rules), and the black-box step must not pass by skipping on CI.
summary_check() {
  local log=$1 mode=${2:-}
  grep -qiE "(total: [1-9]|Total: [1-9])" "$log" || { echo "No tests ran."; return 1; }
  if [ "$mode" = --no-skips ] && grep -qiE "skipped: [1-9]" "$log"; then
    echo "Tests were skipped in a step where every test must run on CI:"
    grep -E "^skipped |\[SKIP\]" "$log" | head -40
    return 1
  fi
}

dotnet_test() {
  local mode=$1
  shift
  local log
  log=$(mktemp)
  (cd "$repo" && timed "$@") 2>&1 | tee "$log"
  local status=${PIPESTATUS[0]}
  [ "$status" -eq 0 ] && summary_check "$log" "$mode" || status=1
  rm -f "$log"
  return "$status"
}

aot() {
  local project=$1 name
  shift
  name=$(basename "$project")
  (cd "$repo" && timed dotnet publish "$project" -c Release -p:SparTestLane=Aot -o "artifacts/aot/$name") || return 1
  mkdir -p "$repo/artifacts/test-results/$name"
  local trx="artifacts/test-results/$name/aot-$(date -u +%Y-%m-%d_%H_%M_%S).trx"
  dotnet_test "" "${switch[@]}" "artifacts/aot/$name/$name" -result-trx "$trx" "$@"
}

step() {
  case $1 in
    copy)
      rm -rf "$repo" && mkdir -p "$repo" || return 1
      tar -C /src --exclude=./.git --exclude=./artifacts --exclude=./spikes --exclude=bin --exclude=obj --exclude=node_modules -cf - . \
        | tar -C "$repo" -xf - || return 1
      echo "copied $(cd "$repo" && find src tests samples -name '*.cs' | wc -l) C# files" ;;
    client) (cd "$repo/src/Spar.Client" && timed npm ci --ignore-scripts && timed npm run build) ;;
    build)
      (cd "$repo" && timed dotnet build tests/Spar.WindowTests -c Debug && timed dotnet build samples/Notebook.EndToEndTests -c Debug) ;;
    check)
      # What the window tests' first window would find, said plainly: bwrap's namespaces, then the switch's.
      echo "Security profile: $( { tr -d '\0' </proc/self/attr/apparmor/current || tr -d '\0' </proc/self/attr/current; } 2>/dev/null || echo none)"
      timed bwrap --unshare-all --die-with-parent --ro-bind / / true \
        || { echo "bwrap cannot create its namespaces or mount in them: run the container with --security-opt seccomp=unconfined and, where the host has AppArmor, --security-opt apparmor=spar-linux-ci (eng/linux-ci/apparmor-profile)."; return 1; }
      # What WebKit's web process sandbox also does: a new /proc in its own PID namespace, and a /dev of its own; then
      # the same under the network switch, where the window tests run (the mounted tree's switch: this step runs before
      # copy).
      local sandbox=(bwrap --unshare-all --die-with-parent --ro-bind / / --proc /proc --dev /dev true)
      timed "${sandbox[@]}" || {
        echo "bwrap cannot mount a new /proc or /dev, as WebKit's sandbox does (a masked /proc needs --security-opt systempaths=unconfined). The mounts on /proc:"
        grep ' /proc[/ ]' /proc/self/mountinfo
        return 1
      }
      timed sh /src/eng/linux-ci/network-switch.sh sh -c 'test -p "$SPAR_TEST_NETWORK_SWITCH" && ip route show default | grep -q "dev lo"' \
        || { echo "The network switch did not start (user and network namespaces)."; return 1; }
      timed sh /src/eng/linux-ci/network-switch.sh "${sandbox[@]}" \
        || { echo "Under the network switch, bwrap cannot mount a new /proc or /dev."; return 1; } ;;
    window)
      dotnet_test "" "${switch[@]}" dotnet test --project tests/Spar.WindowTests -c Debug --no-build --filter-trait "Surface=Window" || return 1
      dotnet_test "" "${switch[@]}" dotnet test --project samples/Notebook.EndToEndTests -c Debug --no-build --filter-trait "Surface=Window" ;;
    windowaot)
      aot tests/Spar.WindowTests -trait "Surface=Window" || return 1
      aot samples/Notebook.EndToEndTests -trait "Surface=Window" ;;
    apps)
      # The executables the BridgeProbe tests take beside the Production probe the test build publishes: its Development
      # build (automated mode, triggers, scripted answers), its build with csp.AllowWebAssembly() (page security is read
      # by the build, so it is an executable of its own, built from a copy of the folder) and the Notebook.
      (cd "$repo" && timed dotnet build tests/apps/BridgeProbe -c Debug -p:SparDevServer=false) || return 1
      rm -rf "$repo/tests/apps/BridgeProbeWasm" && mkdir -p "$repo/tests/apps/BridgeProbeWasm" || return 1
      tar -C "$repo/tests/apps/BridgeProbe" --exclude=./bin --exclude=./obj -cf - . | tar -C "$repo/tests/apps/BridgeProbeWasm" -xf - || return 1
      (cd "$repo" && timed dotnet publish tests/apps/BridgeProbeWasm -c Release -r linux-x64 -p:BridgeProbeWebAssembly=true -o "$apps/probe-wasm") || return 1
      (cd "$repo" && timed dotnet publish samples/Notebook -c Release -r linux-x64 -o "$apps/notebook") || return 1
      ls -l "$dev_probe" "$apps/probe-wasm/BridgeProbe" "$apps/notebook/Notebook" ;;
    blackbox)
      # Every black-box test that can run here, on the Production probe: not those that need the Development build
      # (Probe=Development, blackboxdev's), Windows' own mechanisms (OS=Windows), the measurements (Kind=Measurement), nor
      # what a container keeps read-only (Needs=WritableProcSys: a real denial of user namespaces writes /proc/sys).
      (export SPAR_LINUX_WASM_APP=$apps/probe-wasm/BridgeProbe SPAR_LINUX_NOTEBOOK=$apps/notebook/Notebook
        dotnet_test --no-skips dotnet test --project tests/Spar.WindowTests -c Debug --no-build --filter-trait "Surface=BlackBox" \
          --filter-not-trait "Probe=Development" --filter-not-trait "OS=Windows" --filter-not-trait "Kind=Measurement" \
          --filter-not-trait "Needs=WritableProcSys") || return 1
      dotnet_test --no-skips dotnet test --project samples/Notebook.EndToEndTests -c Debug --no-build --filter-trait "Surface=BlackBox" ;;
    blackboxdev)
      # Only the tests that need the probe's Development build (its automated mode, triggers, heartbeat interval):
      # blackbox ran every other one on the Production probe, which is what apps ship. Running them all again on the
      # Development build took 5.6 minutes of the job.
      (export SPAR_LINUX_HOST_APP=$dev_probe SPAR_LINUX_WASM_APP=$apps/probe-wasm/BridgeProbe
        dotnet_test --no-skips dotnet test --project tests/Spar.WindowTests -c Debug --no-build --filter-trait "Probe=Development") ;;
    *) echo "unknown step $1"; return 2 ;;
  esac
}

status=0
for name in "$@"; do
  echo "STEP $name on $(uname -r), dotnet $(dotnet --version 2>/dev/null), $(id -un) uid $(id -u)"
  step "$name" || { status=1; echo "STEP $name failed"; }
done
exit $status
