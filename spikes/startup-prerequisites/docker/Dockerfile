# Startup prerequisites spike, Linux test images (Ubuntu 24.04). Each target is a machine with a different set of
# pieces; finer breakage (a helper folder, bwrap, xmessage) is removed per run by docker/in-container.sh. Every apt step
# logs apt's own download and disk summary under /opt/spar-apt/.
#   base    Xvfb, openbox, the X11 tools the harness uses (xdotool, xwd, netpbm), D-Bus and socat. No GTK at all.
#   nogtk   base + helpers a system without GTK 4 may have: kdialog (KDE), yad (GTK 3), dunst + notify-send.
#   gtk4    base + GTK 4 (no WebKit) + zenity, dunst, notify-send, and sway + grim for a headless Wayland session.
#   webkit  gtk4 + WebKitGTK 6.0 from noble-updates (brings bubblewrap and xdg-dbus-proxy).
#   webkit-old  gtk4 + WebKitGTK 6.0 2.44.0 from the noble release pocket (a real older version).
FROM ubuntu:24.04 AS base
ENV DEBIAN_FRONTEND=noninteractive
RUN mkdir -p /opt/spar-apt && apt-get update -qq \
 && apt-get install -y --no-install-recommends xvfb xauth openbox x11-utils x11-apps xdotool xfonts-base netpbm socat procps \
      dbus dbus-x11 fonts-dejavu-core ca-certificates 2>&1 | tee /opt/spar-apt/base.log | grep -E "^(Need|After)"

FROM base AS nogtk
RUN apt-get install -y --no-install-recommends kdialog yad dunst libnotify-bin adwaita-icon-theme 2>&1 \
      | tee /opt/spar-apt/nogtk.log | grep -E "^(Need|After)"

FROM base AS gtk4
RUN apt-get install -y --no-install-recommends libgtk-4-1 zenity dunst libnotify-bin adwaita-icon-theme sway grim 2>&1 \
      | tee /opt/spar-apt/gtk4.log | grep -E "^(Need|After)"

FROM gtk4 AS webkit
RUN apt-get install -y --no-install-recommends libwebkitgtk-6.0-4 2>&1 | tee /opt/spar-apt/webkit.log | grep -E "^(Need|After)"

FROM gtk4 AS webkit-old
RUN apt-cache madison libwebkitgtk-6.0-4 libjavascriptcoregtk-6.0-1 | tee /opt/spar-apt/webkit-old-madison.log \
 && apt-get install -y --no-install-recommends libwebkitgtk-6.0-4=2.44.0-2 libjavascriptcoregtk-6.0-1=2.44.0-2 2>&1 \
      | tee /opt/spar-apt/webkit-old.log | grep -E "^(Need|After|E:)"
