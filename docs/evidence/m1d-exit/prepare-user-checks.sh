#!/usr/bin/env bash
# M1d's exit: prepares the user's checklist (user-checklist.md) on Windows, so each check starts at once. From Git Bash
# at the repository's root:
#   bash docs/evidence/m1d-exit/prepare-user-checks.sh
# It opens no window and starts no app. What it makes, all outside the repository except the Notebook's executable:
#   %TEMP%\spar-guide-m1d6b\feed, nuget.config   Spar's nine packages from this tree (../m1d-spar-tool/guide-win.sh pack)
#   %TEMP%\spar-guide-m1d6b\work\Notes           a new app from the template, built once in Debug, its tool restored
#                                                (guide-win.sh new and restore): the IDE checks
#   %TEMP%\spar-guide-m1d6b\work\Init            a new app whose Init\Notes\frontend is removed: spar init frontend
#   %TEMP%\spar-guide-m1d6b\work\Theme           a new app without the end-to-end project, its splash uncommented, its
#                                                first render held 1.5 s (a slow start on purpose) and three buttons that
#                                                call setColorScheme; published (Production NativeAOT) to
#                                                Theme\Notes\bin\Production\Release\net11.0\win-x64\publish\Notes.exe
#   artifacts\m1d-exit\notebook-win\Notebook.exe the Notebook's Production executable (win.sh notebook)
# NuGet's and npm's caches are the package tests' (%TEMP%\spar-package-tests), the template hive and the .NET CLI's home
# the guide run's. Logs: artifacts/m1d-exit/user-checks/.
set -uo pipefail
repo=$(cd "$(dirname "$0")/../../.." && pwd)
logs=$repo/artifacts/m1d-exit/user-checks
G=$(cygpath -u "$TEMP")/spar-guide-m1d6b
shared=$(cygpath -u "$TEMP")/spar-package-tests
mkdir -p "$logs"
guide=$repo/docs/evidence/m1d-spar-tool/guide-win.sh
export NUGET_PACKAGES=$(cygpath -w "$shared/nuget-packages") NUGET_HTTP_CACHE_PATH=$(cygpath -w "$shared/http-cache") npm_config_cache=$(cygpath -w "$shared/npm-cache")
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=false
export DOTNET_CLI_HOME=$(cygpath -w "$G/dotnet-cli-home")

{
  bash "$guide" pack || exit 1
  bash "$guide" new || exit 1
  bash "$guide" restore || exit 1
  hive=$(cygpath -w "$G/hive")

  # spar init frontend: an app with no frontend folder, its tool restored.
  cd "$G/work" && rm -rf Init
  dotnet new spar-app -n Notes -o Init --debug:custom-hive "$hive" --no-update-check || exit 1
  rm -rf Init/Notes/frontend
  (cd Init && dotnet tool restore) || exit 1

  # The start-up page on an on-screen window: the template's splash, a slow first render, setColorScheme's buttons.
  cd "$G/work" && rm -rf Theme
  dotnet new spar-app -n Notes -o Theme --no-end-to-end-tests --debug:custom-hive "$hive" --no-update-check || exit 1
  page=$G/work/Theme/Notes/frontend
  # Uncomment the splash: drop the comment's opening line through its last line of prose, and its closing line.
  sed -i -e '/^    <!--$/,/^      in src\/styles.css; the timing is set with spar/d' -e '/^    -->$/d' "$page/index.html"
  grep -q '<div id="spar-splash"' "$page/index.html" && ! grep -q '<!--' "$page/index.html" || { echo "the splash was not uncommented"; exit 1; }
  # Hold the first render 1.5 s, so every start is a slow one and the splash shows.
  sed -i 's/^createRoot(document.getElementById("root")!).render(/setTimeout(() => createRoot(document.getElementById("root")!).render(/; s/^);$/), 1500);/' "$page/src/main.tsx"
  grep -q '), 1500);' "$page/src/main.tsx" || { echo "the first render was not delayed"; exit 1; }
  # Three buttons that call setColorScheme, under the greeting.
  sed -i 's/^import { greet } from ".\/greet.js";$/import { setColorScheme } from "@spar\/app\/sdk";\nimport { greet } from ".\/greet.js";/' "$page/src/App.tsx"
  sed -i 's|^      <p role="status">{message}</p>$|      <p role="status">{message}</p>\n      <p>\n        {(["light", "dark", "system"] as const).map(scheme => (\n          <button key={scheme} type="button" onClick={() => void setColorScheme(scheme).match({ success: () => setMessage(`Scheme: ${scheme}`), errors: { notSaved: () => setMessage(`Scheme ${scheme}, not saved`) }, failure: error => setMessage(`Scheme failed (${error.code})`) })}>\n            {scheme}\n          </button>\n        ))}\n      </p>|' "$page/src/App.tsx"
  grep -q 'setColorScheme(scheme)' "$page/src/App.tsx" || { echo "the buttons were not added"; exit 1; }
  (cd Theme && dotnet build Notes -nologo -v:m | tail -3) || exit 1
  (cd Theme && dotnet publish Notes -c Release -nologo -v:m | tail -2) || exit 1
  ls -l "$G/work/Theme/Notes/bin/Production/Release/net11.0/win-x64/publish/Notes.exe" || exit 1

  # The Notebook's Production executable, from this tree.
  bash "$repo/docs/evidence/m1d-exit/win.sh" notebook || exit 1
  echo "prepared"
} 2>&1 | tee "$logs/prepare.log"
exit "${PIPESTATUS[0]}"
