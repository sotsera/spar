# Shared by the run scripts: the published spike executable and the results folder, relative to this script.
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
spike="$(cd "$here/.." && pwd)"
exe="$spike/Profiles.Win/bin/publish/Profiles.Win.exe"
results="$spike/results"
bulky="$(cd "$spike/../.." && pwd)/artifacts/webview-profiles"
mkdir -p "$results" "$bulky"
if [ ! -x "$exe" ]; then
  echo "Publish first: dotnet publish Profiles.Win -c Release -o Profiles.Win/bin/publish" >&2
  exit 1
fi
