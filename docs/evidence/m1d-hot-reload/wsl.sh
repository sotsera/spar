#!/usr/bin/env bash
# M1d step 3's Linux runs in WSL (Ubuntu-24.04, the normal user, no root): docs/evidence/m1d-hot-reload.md.
# From Git Bash on Windows, at the repository's root (MSYS_NO_PATHCONV keeps the /mnt path as it is):
#   MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/<repo>/docs/evidence/m1d-hot-reload/wsl.sh <step>...
# Steps:
#   watch    watch-run.mjs: `dotnet watch --non-interactive -lp Loopback` on the Debug Notebook, its page in Playwright's
#            headless Chromium (the copy the repository's browser tests use; never WSLg's display, never a browser of
#            the user's), then every edit: body, toastoff, dto, error, command, limit
#   desktop  `dotnet watch -lp Desktop` on a private Xvfb (:97, an abstract socket only) with a private D-Bus session:
#            the window's page reports ui-ready, then a method-body edit is applied, the host sends backend-updated
#            (app.log) and the page's probe (added to main.tsx for the run) reports the event with the toasts it sees,
#            then a DTO edit restarts the app (a second ui-ready); dotnet watch is stopped with SIGTERM
#   interactive, interactivenoshape
#            dotnet watch as a terminal runs it (interactive, in a pseudo-terminal), an error-case edit, and what it
#            prints; `interactivenoshape` with the copy's generator leaving out the contracts' shape, as the control
# They run on the copy ../m1c-blackbox/wsl.sh keeps in /var/tmp/spar-m1c-blackbox/repo, after its `sync client build`
# steps. DISPLAY and WAYLAND_DISPLAY are unset first. Each run's HOME and XDG folders are under the copy's
# artifacts/linux-homes/m1d-hot-reload/<step>/.
set -uo pipefail
# No core dumps (decision "Linux test runs write no core dumps"): a limit of exactly 1 byte; children inherit it.
[ "${SPAR_CORE_DUMPS:-}" = 1 ] || prlimit --core=1 --pid $$
source_repo=$(cd "$(dirname "$0")/../../.." && pwd)
repo=/var/tmp/spar-m1c-blackbox/repo
tools=/var/tmp/spar-m1c-host/tools
cache=/var/tmp/spar-linux/cache
homes=$repo/artifacts/linux-homes/m1d-hot-reload
logs=$source_repo/artifacts/linux-runs/m1d-hot-reload
mkdir -p "$logs"

export DOTNET_ROOT=$cache/dotnet PATH=$cache/dotnet:$PATH
export NUGET_PACKAGES=$cache/nuget-packages NUGET_HTTP_CACHE_PATH=$cache/nuget-http npm_config_cache=$cache/npm
export PLAYWRIGHT_BROWSERS_PATH=$cache/ms-playwright
export LD_LIBRARY_PATH=$cache/libs/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
unset DISPLAY WAYLAND_DISPLAY GSK_RENDERER GTK_DEBUG SPAR_AUTOMATED DBUS_SESSION_BUS_ADDRESS CI BROWSER
for v in $(env | grep -o '^\(JSC_\|SPAR_\|WEBKIT_\)[^=]*' || true); do unset "$v"; done

timed() {
  local start=$(date +%s%N)
  "$@"
  local exit=$?
  echo "STEP exit $exit in $(( ($(date +%s%N) - start) / 1000000 )) ms: $*"
  return $exit
}

home_for() {
  local root=$homes/$1
  rm -rf "$root" && mkdir -p "$root"/{home,data,config,cache,state} && mkdir -p "/dev/shm/spar-m1d-hr-$1" && chmod 700 "/dev/shm/spar-m1d-hr-$1"
  echo "HOME=$root/home XDG_DATA_HOME=$root/data XDG_CONFIG_HOME=$root/config XDG_CACHE_HOME=$root/cache XDG_STATE_HOME=$root/state XDG_RUNTIME_DIR=/dev/shm/spar-m1d-hr-$1"
}

step() {
  case $1 in
    watch)
      local package
      package=$(find "$repo/samples/Notebook.EndToEndTests/bin/Development/Debug" -path '*/.playwright/package' -type d | head -1)
      local node_bin
      node_bin=$(command -v node || echo "$package/../node/linux-x64/node")
      (cd "$repo" && timed "$node_bin" "$source_repo/docs/evidence/m1d-hot-reload/watch-run.mjs" --project samples/Notebook \
        --root "$homes/watch" --log "$logs/watch.run.log" --playwright "$package" \
        --edits "${EDITS:-body,toastoff,dto,error,command,limit}" --verbose true)
      grep -E "PASS|FAIL|failures" "$logs/watch.run.log" ;;
    desktop)
      LD_LIBRARY_PATH=$tools/root/usr/lib/x86_64-linux-gnu bwrap --die-with-parent --dev-bind / / --bind /usr/bin "$tools/realbin" \
        --bind "$tools/usrbin" /usr/bin -- "$tools/root/usr/bin/Xvfb" :97 -screen 0 1600x1000x24 -nolisten tcp -nolisten unix \
        >"$logs/xvfb.log" 2>&1 &
      local xvfb=$!
      for _ in $(seq 1 100); do grep -q "@/tmp/.X11-unix/X97\$" /proc/net/unix && break; sleep 0.05; done
      local out=$logs/desktop.out.log
      local store=$repo/samples/Notebook/Pages/MarkdownPageStore.cs types=$repo/samples/Notebook/Pages/PageTypes.cs
      cp "$store" "$logs/store.orig" && cp "$types" "$logs/types.orig"
      # The page's probe (as WatchDesktop.cs's): 50 ms after each update, a request through the app's origin naming the
      # revision and the toasts the overlay shows; the proxy's Debug line in app.log carries the path.
      local main=$repo/samples/Notebook/frontend/src/main.tsx
      cp "$main" "$logs/main.orig"
      cat >"$logs/probe.ts" <<'PROBE'
import { onBackendUpdate } from "@spar/app/sdk";
onBackendUpdate(u => setTimeout(() => {
  const n = document.querySelector("[data-spar-dev-overlay]")?.shadowRoot?.querySelectorAll(".toast").length ?? 0;
  new Image().src = `/__spar-probe/backend-updated/${u.revision}/toasts-${n}`;
}, 50));
PROBE
      sed -i "/^import \".\/styles.css\";\$/r $logs/probe.ts" "$main"
      grep -c "__spar-probe" "$main"
      local env_line
      env_line=$(home_for desktop)
      (cd "$repo" && exec env $env_line DISPLAY=:97 GDK_BACKEND=x11 MSBUILDDISABLENODEREUSE=1 DOTNET_WATCH_SUPPRESS_EMOJIS=1 \
        setsid dbus-run-session -- dotnet watch --non-interactive --verbose --project samples/Notebook -lp Desktop >"$out" 2>&1) &
      local runner=$!
      local app_log=$homes/desktop/data/com.example.Notebook.Development/logs/app.log
      for _ in $(seq 1 1800); do [ -f "$app_log" ] && grep -q "ui-ready" "$app_log" && break; sleep 0.1; done
      sleep 2
      local t0=$(date +%s%N)
      sed -i 's/new PageSummary(page.Id, page.Title, page.UpdatedAt)/new PageSummary(page.Id, page.Title + " (edited)", page.UpdatedAt)/' "$store"
      for _ in $(seq 1 300); do grep -q "Hot Reload applied revision 1" "$app_log" && break; sleep 0.05; done
      echo "body edit to the host's line: $(( ($(date +%s%N) - t0) / 1000000 )) ms (polled every 50 ms)"
      grep -E "Hot Reload applied revision" "$app_log"
      for _ in $(seq 1 100); do grep -q "__spar-probe/backend-updated/1/" "$app_log" && break; sleep 0.05; done
      grep -o "GET /__spar-probe/[^ ]* [0-9]*" "$app_log"
      cp "$logs/store.orig" "$store"
      sleep 3
      local t1=$(date +%s%N)
      sed -i 's/PageSummary(string Id, string Title, DateTimeOffset UpdatedAt)/PageSummary(string Id, string Title, DateTimeOffset UpdatedAt, string? Excerpt = null)/' "$types"
      sed -i 's/\(const \(older\|newer\): PageSummary = {[^}]*\) };/\1, excerpt: null };/' "$repo/samples/Notebook/frontend/src/notebook.test.tsx"
      for _ in $(seq 1 1800); do [ "$(grep -c "ui-ready at" "$app_log")" -ge 2 ] && break; sleep 0.1; done
      echo "DTO edit to the restarted window's ui-ready: $(( ($(date +%s%N) - t1) / 1000000 )) ms (polled every 100 ms)"
      grep -E "ui-ready at|Restart is needed|ENC0020|Terminating process|exited with exit code|Exited" "$out" "$app_log" | cut -c1-240
      cp "$logs/store.orig" "$store" && cp "$logs/types.orig" "$types" && cp "$logs/main.orig" "$main"
      cp "$source_repo/samples/Notebook/frontend/src/notebook.test.tsx" "$repo/samples/Notebook/frontend/src/notebook.test.tsx"
      kill -TERM -- -"$runner" 2>/dev/null || kill -TERM "$runner"
      wait "$runner" 2>/dev/null
      sleep 2
      echo "left in the run's session: $(pgrep -s "$runner" | wc -l)"
      pkill -KILL -s "$runner" 2>/dev/null
      local vite
      vite=$(grep -o '"pid":[0-9]*' "$repo/samples/Notebook/obj/spar-dev-server.json" 2>/dev/null | cut -d: -f2)
      [ -n "$vite" ] && kill "$vite" 2>/dev/null && echo "vite $vite ended"
      kill "$xvfb" 2>/dev/null; wait "$xvfb" 2>/dev/null
      rm -rf /dev/shm/spar-m1d-hr-desktop ;;
    interactive | interactivenoshape)
      # dotnet watch as a developer's terminal runs it: interactive (no --non-interactive, no DOTNET_WATCH_RESTART_ON_RUDE_EDIT),
      # in a pseudo-terminal (script) whose input is a FIFO this step holds open and never writes, so no prompt is answered.
      # An error case's payload is edited (TitleTaken's Title made nullable) and the output read 25 s later. With
      # `interactivenoshape` the copy's generator first leaves out the contracts' shape (Spar.ContractShape.g.cs), as a
      # control: what dotnet watch does with such an edit without Spar's restart rule. The copy is restored after.
      local generator=$repo/src/Spar.Generator/SparGenerator.cs types=$repo/samples/Notebook/Pages/PageTypes.cs
      cp "$generator" "$logs/generator.orig" && cp "$types" "$logs/types.orig"
      if [ "$1" = interactivenoshape ]; then
        sed -i 's/        if (development)$/        if (development \&\& source.Length < 0)/' "$generator"
        grep -c "source.Length < 0" "$generator"
      fi
      local env_line
      env_line=$(home_for "$1")
      local fifo=/dev/shm/spar-m1d-hr-$1/input
      mkfifo "$fifo"
      exec 7<>"$fifo"
      local out=$logs/$1.out.log
      (cd "$repo" && exec env $env_line SPAR_HOST=Loopback SPAR_LOOPBACK_BROWSER=none MSBUILDDISABLENODEREUSE=1 DOTNET_WATCH_SUPPRESS_EMOJIS=1 \
        setsid script -qfec "dotnet watch --verbose --project samples/Notebook -lp Loopback" /dev/null <"$fifo" >"$out" 2>&1) &
      local runner=$!
      for _ in $(seq 1 1800); do grep -q "loopback listening" "$out" && break; sleep 0.1; done
      sleep 3
      sed -i 's/TitleTaken(string Title)/TitleTaken(string? Title)/' "$types"
      echo "edit written at $(date +%T.%N)"
      sleep 25
      grep -a -E "File updated|changes applied|Restart is needed|restart your app|ENC0[0-9]+|Hot Reload applied revision|rude edit" "$out" | cut -c1-260
      cp "$logs/types.orig" "$types" && cp "$logs/generator.orig" "$generator"
      kill -TERM -- -"$runner" 2>/dev/null || kill -TERM "$runner"
      wait "$runner" 2>/dev/null
      sleep 2
      echo "left in the run's session: $(pgrep -s "$runner" | wc -l)"
      pkill -KILL -s "$runner" 2>/dev/null
      exec 7>&-
      local vite
      vite=$(grep -o '"pid":[0-9]*' "$repo/samples/Notebook/obj/spar-dev-server.json" 2>/dev/null | cut -d: -f2)
      [ -n "$vite" ] && kill "$vite" 2>/dev/null && echo "vite $vite ended"
      (cd "$repo" && dotnet build samples/Notebook -c Debug -v:q -nologo | tail -2)
      rm -rf "/dev/shm/spar-m1d-hr-$1" ;;
    *) echo "unknown step $1"; return 2 ;;
  esac
}

status=0
for name in "$@"; do
  { echo "STEP $name on $(uname -r), dotnet $(dotnet --version 2>/dev/null)"; step "$name"; } 2>&1 | tee "$logs/$name.log"
  [ "${PIPESTATUS[0]}" -eq 0 ] || status=1
done
exit $status
