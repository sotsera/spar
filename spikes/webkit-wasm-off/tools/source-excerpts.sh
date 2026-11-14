#!/usr/bin/env bash
# Downloads the WebKit sources this spike cites (tag webkitgtk-2.52.6, plus the CSP fix in 2.54.0) into
# artifacts/webkit-wasm-off/src/ (disposable) and writes the cited lines to results/source-excerpts.txt.
# Usage (from the spike folder): bash tools/source-excerpts.sh
set -euo pipefail
spike="$(cd "$(dirname "$0")/.." && pwd)"
src="$spike/../../artifacts/webkit-wasm-off/src"
mkdir -p "$src/2.52.6" "$src/2.54.0" "$src/other"
raw=https://raw.githubusercontent.com/WebKit/WebKit
get() { [ -s "$src/$1/$(basename "$3")" ] || curl -sS -f -L -o "$src/$1/$(basename "$3")" "$raw/$2/$3"; }
for f in Source/JavaScriptCore/runtime/OptionsList.h Source/JavaScriptCore/runtime/Options.cpp \
         Source/JavaScriptCore/wasm/js/JSWebAssembly.cpp Source/JavaScriptCore/wasm/js/JSWebAssemblyInstance.cpp \
         Source/JavaScriptCore/API/glib/JSCOptions.cpp Source/WebCore/page/csp/ContentSecurityPolicy.cpp \
         Source/WebCore/page/csp/ContentSecurityPolicyDirectiveList.cpp Source/WebKit/UIProcess/API/glib/WebKitWebContext.cpp \
         Source/WebKit/UIProcess/Launcher/glib/BubblewrapLauncher.cpp; do
  get 2.52.6 webkitgtk-2.52.6 "$f"
done
get 2.54.0 webkitgtk-2.54.0 Source/JavaScriptCore/wasm/js/JSWebAssembly.cpp
get other webkitgtk-2.44.0 Source/JavaScriptCore/runtime/OptionsList.h
out="$spike/results/source-excerpts.txt"
s="$src/2.52.6"
{
  echo "# Lines cited by RESULTS.md. Files from https://github.com/WebKit/WebKit at the tag in each heading."
  echo; echo "## webkitgtk-2.52.6 Source/JavaScriptCore/runtime/OptionsList.h: the WebAssembly options"
  grep -n -E 'v\(Bool, (useWasm|useWasmIPInt|useBBQJIT|useOMGJIT|failToCompileWasmCode|useJIT),' "$s/OptionsList.h"
  echo; echo "## webkitgtk-2.44.0 OptionsList.h: the older name"
  grep -n -E 'v\(Bool, useWebAssembly,' "$src/other/OptionsList.h"
  echo; echo "## webkitgtk-2.52.6 Options.cpp: JSC_ environment variables read on Linux, invalid names only logged"
  grep -n -E 'PLATFORM\(COCOA\) \|\| OS\(LINUX\)|strncmp\("JSC_"|invalid option|hasBadOptions && Options::validateOptions|static inline void disableAllWasmOptions|if \(!Options::useWasm\(\)\)|INCOHERENT OPTIONS: at least one of useWasmIPInt' "$s/Options.cpp"
  echo; echo "## webkitgtk-2.52.6 JSCOptions.cpp: jsc_options_set_boolean writes the option without recomputing dependants"
  grep -n -E '^gboolean jsc_options_set_boolean|Options::name_\(\) = valueToSet' "$s/JSCOptions.cpp"
  echo; echo "## webkitgtk-2.52.6 CSP: WebAssembly disabled together with eval, checked only when an instance is created"
  (cd "$s" && grep -n -E 'Refused to create a WebAssembly object|setWebAssemblyEnabled\(m_last|disableWebAssembly\(m_last' ContentSecurityPolicyDirectiveList.cpp ContentSecurityPolicy.cpp | cut -c1-220)
  (cd "$s" && grep -n -E 'webAssemblyEnabled' JSWebAssemblyInstance.cpp JSWebAssembly.cpp) || true
  echo "(JSWebAssembly.cpp at 2.52.6: $(grep -c webAssemblyEnabled "$s/JSWebAssembly.cpp" || true) checks)"
  echo; echo "## webkitgtk-2.54.0 JSWebAssembly.cpp: the checks added by commit 4003087f20 (2026-05-28)"
  grep -n -E 'webAssemblyEnabled' "$src/2.54.0/JSWebAssembly.cpp"
  echo; echo "## webkitgtk-2.52.6 WebKitWebContext.cpp: the extensions directory is added to the sandbox by the setter"
  grep -n -E 'webProcessExtensionsDirectory = directory|addSandboxPath\(directory|g_signal_emit\(context, signals\[INITIALIZE_WEB_PROCESS_EXTENSIONS\]' "$s/WebKitWebContext.cpp"
  echo; echo "## webkitgtk-2.52.6 BubblewrapLauncher.cpp: no --clearenv (the environment is inherited)"
  echo "clearenv occurrences: $(grep -c -- 'clearenv' "$s/BubblewrapLauncher.cpp" || true)"
} > "$out"
echo "wrote $out"
