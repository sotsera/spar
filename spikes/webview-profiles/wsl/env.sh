# Sourced by every WSL script (copied from spikes/linux-memory, own working folder). Points all per-user state at a
# private folder ($SPAR_WORK, default ~/spar-spikes/webview-profiles) before any GTK, WebKit or dotnet process starts.
if [ -z "${SPAR_ENV_DONE:-}" ]; then
  SPAR_WORK=${SPAR_WORK:-$HOME/spar-spikes/webview-profiles}
  export SPAR_WORK SPAR_REAL_HOME=$HOME
  export HOME=$SPAR_WORK/home
  export XDG_CONFIG_HOME=$HOME/.config XDG_DATA_HOME=$HOME/.local/share XDG_CACHE_HOME=$HOME/.cache XDG_STATE_HOME=$HOME/.local/state
  export XDG_RUNTIME_DIR=$SPAR_WORK/run
  mkdir -p "$XDG_CONFIG_HOME" "$XDG_DATA_HOME" "$XDG_CACHE_HOME" "$XDG_STATE_HOME" "$XDG_RUNTIME_DIR"
  chmod 700 "$XDG_RUNTIME_DIR"
  export DOTNET_CLI_HOME=$HOME DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
  export NUGET_PACKAGES=$SPAR_WORK/nuget/packages
  # The SDK is used read-only from the real home; nothing is written there.
  export PATH="$SPAR_REAL_HOME/.dotnet:$PATH"
  unset WEBKIT_INSPECTOR_SERVER WEBKIT_INSPECTOR_HTTP_SERVER
  export SPAR_ENV_DONE=1
fi
