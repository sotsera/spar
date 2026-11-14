#!/usr/bin/env bash
# Host side (Git Bash on Windows, Docker with Linux containers). Usage, from any folder:
#   run.sh up                 network + two SQL Server containers + the harness container
#   run.sh exec <steps...>    run inside.sh steps in the harness (sync build strict wait setup bulk bulk-cancel
#                             query-cancel read schema monitor probes secret deps keyring)
#   run.sh windows            same-session reference: the Windows exes of spikes/sqlclient-tool (run, not modified)
#                             against the same containers through their published loopback ports
#   run.sh down               remove the containers and the network (logs saved first)
#   run.sh all                up, every step, windows, down (down also runs on failure)
# - Uses only local images (--pull never). The SA password is generated here, passed with -e NAME (no value on the
#   command line) and never printed or written to a file.
set -euo pipefail
cd "$(dirname "$0")"
export MSYS_NO_PATHCONV=1
spike=$(pwd -W 2>/dev/null || pwd)
repo=$(cd ../.. && (pwd -W 2>/dev/null || pwd))
results=artifacts/results
mkdir -p "$results"

net=spar-sqllinux-net
a=spar-sqllinux-a
b=spar-sqllinux-b
harness=spar-sqllinux-harness
sql_image=mcr.microsoft.com/mssql/server:2022-latest
tool_image=spar-linux-desktop:webkit2.52
nuget=spar-linux-desktop-nuget

up() {
  for c in $a $b $harness; do
    if [ -n "$(docker ps -aq --filter "name=^/$c$")" ]; then echo "container $c exists"; exit 1; fi
  done
  docker network create $net >/dev/null
  local random
  random=$(head -c 400 /dev/urandom | LC_ALL=C tr -dc 'A-Za-z0-9')
  export MSSQL_SA_PASSWORD="Aa1#${random:0:28}"
  for c in $a $b; do
    docker run --pull never -d --name $c --network $net -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD -e MSSQL_PID=Developer \
      -e MSSQL_MEMORY_LIMIT_MB=4096 -p 127.0.0.1::1433 $sql_image >/dev/null
  done
  local common="User ID=sa;Password=$MSSQL_SA_PASSWORD;Encrypt=true;TrustServerCertificate=true"
  export SPAR_SQL_A="Server=tcp:$a,1433;$common"
  export SPAR_SQL_B="Server=tcp:$b,1433;$common"
  docker run --pull never -d --name $harness --network $net --init -e SPAR_SQL_A -e SPAR_SQL_B \
    -v "$repo:/src:ro" -v $nuget:/root/.nuget/packages -v "$spike/artifacts:/out" $tool_image sleep infinity >/dev/null
  unset MSSQL_SA_PASSWORD SPAR_SQL_A SPAR_SQL_B
  echo "up: $(docker ps --filter "name=^/spar-sqllinux-" --format '{{.Names}}' | sort | tr '\n' ' ')"
}

run_inside() { docker exec $harness bash /src/spikes/sqlclient-linux/inside.sh "$@"; }

# The Windows exes read the same connection strings, pointed at the published loopback ports.
windows() {
  local tool=../sqlclient-tool/SqlTool.App/bin
  local pa pb password
  pa=$(docker port $a 1433/tcp | head -1 | sed 's/.*://')
  pb=$(docker port $b 1433/tcp | head -1 | sed 's/.*://')
  password=$(docker exec $harness printenv SPAR_SQL_A | sed 's/.*Password=\([^;]*\);.*/\1/')
  export SPAR_SQL_A="Server=127.0.0.1,$pa;User ID=sa;Password=$password;Encrypt=true;TrustServerCertificate=true"
  export SPAR_SQL_B="Server=127.0.0.1,$pb;User ID=sa;Password=$password;Encrypt=true;TrustServerCertificate=true"
  declare -A exe=([aot]=$tool/aot/Release/net11.0/win-x64/publish/SqlTool.App.exe [jit]=$tool/jit/Release/net11.0/win-x64/SqlTool.App.exe)
  win() {
    local runtime=$1 mode=$2 arg=$3 suffix=${4:-}
    local name="win-$runtime-$mode-$arg${suffix:+-$suffix}" start
    start=$(date +%s%N)
    "${exe[$runtime]}" "$mode" "$arg" >"$results/$name.txt" 2>&1 || true
    echo "[$name] $(( ($(date +%s%N) - start) / 1000000 )) ms"
    grep -E '^(RESULT|FAIL|MEM end)' "$results/$name.txt" | sed 's/^/  /'
  }
  for r in 1 2 3; do for rt in aot jit; do win $rt bulk b100k "r$r"; done; done
  for v in typed-sync seq-async; do for rt in aot jit; do win $rt read $v; done; done
  for rt in aot jit; do win $rt query-cancel waitfor-async; done
  unset SPAR_SQL_A SPAR_SQL_B password
}

down() {
  for c in $a $b; do docker logs $c >"$results/container-$c.log" 2>&1 || true; done
  docker rm -f $a $b $harness >/dev/null 2>&1 || true
  docker network rm $net >/dev/null 2>&1 || true
  echo "down: containers left: $(docker ps -aq --filter 'name=^/spar-sqllinux-' | wc -l)," \
    "network left: $(docker network ls -q --filter "name=^$net$" | wc -l)"
}

case "${1:-}" in
  up) up ;;
  exec) shift; run_inside "$@" ;;
  windows) windows ;;
  down) down ;;
  all)
    trap down EXIT
    up
    run_inside sync build strict wait setup bulk bulk-cancel query-cancel read schema monitor probes secret deps keyring \
      secret
    windows
    ;;
  *) echo "usage: run.sh up|exec <steps>|windows|down|all"; exit 2 ;;
esac
