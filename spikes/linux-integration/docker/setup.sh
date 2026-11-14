#!/usr/bin/env bash
# Usage (in the container, as uid 1000, from /work/spikes/linux-integration): bash docker/setup.sh
# Starts Xvfb :99 (once), builds the X11 helpers and writes an xfce4-panel configuration with one panel at the top
# holding only the Status Tray plugin (systray, which hosts StatusNotifierItems and XEmbed icons).
set -eu
if ! pgrep -u "$(id -u)" -x Xvfb >/dev/null; then
  (Xvfb :99 -screen 0 1280x800x24 -nolisten tcp >/tmp/xvfb.log 2>&1 &)
  sleep 1
fi
clang -O2 -Wall -o docker/xcap docker/xcap.c -lX11 -lz
clang -O2 -Wall -o wsl/xtool wsl/xtool.c -lX11 -lXtst
conf=$HOME/.config/xfce4/xfconf/xfce-perchannel-xml
mkdir -p "$conf"
cat >"$conf/xfce4-panel.xml" <<'XML'
<?xml version="1.0" encoding="UTF-8"?>
<channel name="xfce4-panel" version="1.0">
  <property name="configver" type="int" value="2"/>
  <property name="panels" type="array">
    <value type="int" value="1"/>
    <property name="panel-1" type="empty">
      <property name="position" type="string" value="p=6;x=0;y=0"/>
      <property name="length" type="uint" value="100"/>
      <property name="position-locked" type="bool" value="true"/>
      <property name="size" type="uint" value="32"/>
      <property name="plugin-ids" type="array">
        <value type="int" value="1"/>
      </property>
    </property>
  </property>
  <property name="plugins" type="empty">
    <property name="plugin-1" type="string" value="systray"/>
  </property>
</channel>
XML
echo "setup done: $(xdpyinfo -display :99 2>/dev/null | grep dimensions || echo 'Xvfb :99 running')"
