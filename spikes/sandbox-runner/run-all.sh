#!/usr/bin/env bash
# The spike's measurements, in order (Git Bash, from the repository root). Each session starts and stops its own
# sandbox; the runner refuses to start while another sandbox runs. Timelines: artifacts/sandbox-runner/sessions/*/.
#   spikes/sandbox-runner/run-all.sh publish    publishes the two payloads and the runner
#   spikes/sandbox-runner/run-all.sh cold       3 cold cycles, payload staged after start, then 3 staged before start
#   spikes/sandbox-runner/run-all.sh reuse      one sandbox, 3 x (WireShapes + window tests)
#   spikes/sandbox-runner/run-all.sh edge       deadline, window tests as SYSTEM, a SYSTEM-only cycle, two sandboxes
set -eu
P=artifacts/sandbox-runner/payload
R=artifacts/sandbox-runner/runner/Runner.exe
WS="$P/WireShapes.Tests|WireShapes.Tests.exe|system|300||xunit|local"
WIN="$P/SampleApp.AotTests|SampleApp.AotTests.exe|user|600"
pause() { sleep "${PAUSE:-20}"; }

case "$1" in
  publish)
    dotnet publish tests/WireShapes.Tests -c Release -p:SparTestLane=Aot -o "$P/WireShapes.Tests" -nodeReuse:false
    dotnet publish spikes/sandbox-runner/payload/SampleApp.AotTests -c Release -o "$P/SampleApp.AotTests" -nodeReuse:false
    dotnet publish spikes/sandbox-runner/Runner -c Release -p:PublishAot=true -o artifacts/sandbox-runner/runner -nodeReuse:false
    ;;
  cold)
    for n in 1 2 3; do pause; $R session --name "cold-$n" --job "$WS" --job "$WIN" || true; done
    for n in 1 2 3; do pause; $R session --name "prestaged-$n" --prestage --job "$WS" --job "$WIN" || true; done
    ;;
  reuse)
    pause; $R session --name reuse --job "$WS" --job "$WIN" --job "$WS" --job "$WIN" --job "$WS" --job "$WIN" || true
    ;;
  edge)
    # A deadline shorter than the window tests: the wait ends, the guest process is killed, partial results come back.
    pause; $R session --name deadline --job "$P/SampleApp.AotTests|SampleApp.AotTests.exe|user|8" || true
    # Window tests without a session: SYSTEM, no wsb connect.
    pause; $R session --name system-window --connect never --job "$P/SampleApp.AotTests|SampleApp.AotTests.exe|system|120|-class SampleApp.Tests.WindowTests -method SampleApp.Tests.WindowTests.Page_facts" || true
    # No window tests at all: no session, no client window on the host.
    for n in 1 2 3; do pause; $R session --name "system-only-$n" --connect never --job "$WS" || true; done
    pause; $R two two
    ;;
esac
