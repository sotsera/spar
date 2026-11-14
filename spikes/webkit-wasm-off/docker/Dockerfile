# WebKitGTK 2.54 (Fedora 43) for comparing the CSP WebAssembly checks with the 2.52.6 build in WSL.
# Build: docker build -t spar-webkit-wasm-off:fedora43 docker/   (from the spike folder)
FROM fedora:43
RUN dnf -y --setopt=install_weak_deps=False install \
      webkitgtk6.0-2.54.2 webkitgtk6.0-devel-2.54.2 python3-gobject gtk4 libsoup3 \
      xorg-x11-server-Xvfb bubblewrap xdg-dbus-proxy mesa-dri-drivers clang pkgconf-pkg-config dbus-daemon \
    && dnf clean all \
    && useradd -u 1000 -m probe
# Typelibs the GTK 4 bindings import (cairo-1.0 and others).
RUN dnf -y --setopt=install_weak_deps=False install gobject-introspection && dnf clean all
