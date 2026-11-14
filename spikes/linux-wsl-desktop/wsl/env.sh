#!/usr/bin/env bash
# Usage (inside WSL): bash wsl/env.sh
# Records the WSLg environment this spike ran in: display sockets, packages, tools, GPU, portal and input methods.
set -u
echo "== identity"; id; uname -r; cat /etc/os-release | grep -E '^(PRETTY_NAME|VERSION_ID)='
echo "== session"; for v in DISPLAY WAYLAND_DISPLAY XDG_RUNTIME_DIR DBUS_SESSION_BUS_ADDRESS XDG_SESSION_TYPE XDG_CURRENT_DESKTOP GDK_BACKEND GDK_SCALE GDK_DPI_SCALE QT_SCALE_FACTOR GTK_IM_MODULE XMODIFIERS LIBGL_ALWAYS_SOFTWARE MESA_D3D12_DEFAULT_ADAPTER_NAME GALLIUM_DRIVER; do echo "$v=${!v-}"; done
echo "== wslg"; cat /mnt/wslg/versions.txt 2>/dev/null; ls -la /tmp/.X11-unix/ 2>/dev/null
echo "== packages"
dpkg-query -W -f='${Package} ${Version} ${Status}\n' 'libwebkit2gtk-4.1-0' 'libwebkitgtk-6.0-4' 'libwebkit2gtk-4.1-dev' 'libwebkitgtk-6.0-dev' 'webkit2gtk-driver' 'webkitgtk-6.0-driver' 'libgtk-3-0t64' 'libgtk-4-1' 'libadwaita-1-0' 'clang' 'xdotool' 'libxtst6' 'libx11-dev' 'zlib1g-dev' 'ibus' 'fcitx5' 'fcitx' 'xdg-desktop-portal' 'xdg-desktop-portal-gtk' 'xdg-desktop-portal-gnome' 'bubblewrap' 'xdg-dbus-proxy' 'libgl1-mesa-dri' 'mesa-vulkan-drivers' 'libegl-mesa0' 'strace' 'ltrace' 'x11-apps' 'x11-utils' 'imagemagick' 'mesa-utils' 'weston' 'gstreamer1.0-plugins-good' 'yaru-theme-gtk' 'gnome-themes-extra' 'dbus-user-session' 2>&1 | sed 's/install ok installed/installed/'
echo "== tools"; for t in dotnet node npm clang gcc strace ltrace xdotool xwd xwininfo xprop xdpyinfo import grim wlr-randr busctl gdbus dbus-send WebKitWebDriver python3 gsettings eglinfo glxinfo vulkaninfo bwrap ibus fcitx5 perf; do printf '%-16s %s\n' "$t" "$(command -v $t || echo -)"; done
ls -la ~/.dotnet/dotnet /usr/share/dotnet /usr/lib/dotnet 2>&1 | head -5
echo "== versions"; (dotnet --version; dotnet --list-sdks; node --version; npm --version; clang --version | head -1) 2>&1
echo "== libexec webkit"; ls /usr/lib/x86_64-linux-gnu/webkit2gtk-4.1/ /usr/lib/x86_64-linux-gnu/webkitgtk-6.0/ 2>&1; ls /usr/bin | grep -i -E 'webkit|driver' 
echo "== gpu"; ls -la /dev/dxg /dev/dri 2>&1; ls /usr/lib/wsl/lib 2>&1 | head; ls /usr/lib/x86_64-linux-gnu/dri/ 2>&1 | grep -E 'd3d12|swrast|zink' 
echo "== userns"; cat /proc/sys/kernel/unprivileged_userns_clone 2>&1; cat /proc/sys/user/max_user_namespaces; cat /proc/sys/kernel/apparmor_restrict_unprivileged_userns 2>&1; ls -la /usr/bin/bwrap 2>&1; unshare --user --map-root-user true && echo "unshare -U ok"
echo "== dbus names"; busctl --user list --no-pager 2>&1 | awk '{print $1, $3}' | head -40
echo "== portal"; gdbus call --session --dest org.freedesktop.portal.Desktop --object-path /org/freedesktop/portal/desktop --method org.freedesktop.portal.Settings.ReadOne org.freedesktop.appearance color-scheme 2>&1
echo "== gsettings"; gsettings get org.gnome.desktop.interface color-scheme 2>&1; gsettings get org.gnome.desktop.interface gtk-theme 2>&1; gsettings get org.gnome.desktop.interface scaling-factor 2>&1; gsettings get org.gnome.desktop.interface text-scaling-factor 2>&1
echo "== processes"; ps -eo pid,user,comm | grep -v -E ' (ps|grep|bash|sed|awk)$' | head -40
