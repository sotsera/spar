# Sourced by every WSL script. Points all per-user state at a private folder inside the spike's WSL working folder
# ($SPAR_WORK, default ~/spar-spikes/linux-memory) before any GTK, WebKit, dotnet or npm process starts.
# XDG_RUNTIME_DIR is private too; the display socket is reached through a symlink to the session's own socket.
if [ -z "${SPAR_ENV_DONE:-}" ]; then
  SPAR_WORK=${SPAR_WORK:-$HOME/spar-spikes/linux-memory}
  real_runtime=${XDG_RUNTIME_DIR:?XDG_RUNTIME_DIR must be set (the session runtime folder holds the display socket)}
  export SPAR_WORK SPAR_REAL_HOME=$HOME
  export HOME=$SPAR_WORK/home
  export XDG_CONFIG_HOME=$HOME/.config XDG_DATA_HOME=$HOME/.local/share XDG_CACHE_HOME=$HOME/.cache XDG_STATE_HOME=$HOME/.local/state
  export XDG_RUNTIME_DIR=$SPAR_WORK/run
  mkdir -p "$XDG_CONFIG_HOME" "$XDG_DATA_HOME" "$XDG_CACHE_HOME" "$XDG_STATE_HOME" "$XDG_RUNTIME_DIR"
  chmod 700 "$XDG_RUNTIME_DIR"
  if [ -n "${WAYLAND_DISPLAY:-}" ] && [ -e "$real_runtime/$WAYLAND_DISPLAY" ] && [ ! -e "$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY" ]; then
    ln -s "$real_runtime/$WAYLAND_DISPLAY" "$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY"
  fi
  export DOTNET_CLI_HOME=$HOME DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
  export NUGET_PACKAGES=$SPAR_WORK/nuget/packages
  export npm_config_cache=$SPAR_WORK/npm-cache npm_config_userconfig=$SPAR_WORK/npmrc electron_config_cache=$SPAR_WORK/electron-cache
  export PATH="$SPAR_REAL_HOME/.dotnet:$PATH"
  # No inspector servers, ever.
  unset WEBKIT_INSPECTOR_SERVER WEBKIT_INSPECTOR_HTTP_SERVER
  export SPAR_ENV_DONE=1
fi
