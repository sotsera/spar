#!/usr/bin/env bash
# Runs one test method (JIT) in the container, for diagnosis: one.sh <method filter>
set -euo pipefail
work=/tmp/work; mkdir -p $work/repo; cd /src
tar cf - --exclude=bin --exclude=obj --exclude=results global.json Directory.Build.props Directory.Build.targets Directory.Packages.props .editorconfig spikes/test-host spikes/react/React.Desktop/Interop spikes/linux-desktop/Linux.Desktop/Native.cs | (cd $work/repo && tar xf -)
homes=$work/repo/artifacts/linux-homes/test-host-one; mkdir -p "$homes"/{home,cache,data,config,runtime}; chmod 700 "$homes/runtime"
export HOME=$homes/home XDG_CACHE_HOME=$homes/cache XDG_DATA_HOME=$homes/data XDG_CONFIG_HOME=$homes/config XDG_RUNTIME_DIR=$homes/runtime NUGET_PACKAGES=/nuget
Xvfb :99 -screen 0 1280x1024x24 -nolisten tcp >/dev/null 2>&1 & export DISPLAY=:99 GDK_BACKEND=x11; sleep 1
cd $work/repo/spikes/test-host && dotnet build SampleApp.Tests 2>&1 | grep -E " error |Build succeeded" | sort -u
./SampleApp.Tests/bin/Debug/net11.0/linux-x64/SampleApp.Tests -noLogo -noColor -showLiveOutput -method "$1" 2>&1 | grep -vE "libEGL|^$"
