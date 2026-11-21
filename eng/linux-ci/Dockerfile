# Spar's Linux CI image (decision "M1c's first questions": Spar ships this Dockerfile; CI builds it, nothing is published).
# What the Linux window tests need, and nothing they don't: Ubuntu 24.04, the .NET SDK that global.json names, Node 24,
# WebKitGTK 6.0 with GTK 4, Xvfb with XTEST, a D-Bus session daemon, at-spi2-core (the black box reads windows over
# AT-SPI), fonts, bubblewrap and xdg-dbus-proxy for the web process's sandbox, clang and zlib for NativeAOT,
# iproute2 for the network switch of the online/offline test, and python3-gi for the theme test's fake portal. The
# user is `spar`, uid 1000: WebKit's web-process sandbox (bwrap) refuses to start as root in a container.
#
# Run it with --init (PID 1 reaps the app processes the tests start), --shm-size (WebKit's shared memory) and a seccomp
# profile that allows user namespaces (bwrap); eng/linux-ci/run.sh has the command and the steps CI runs in it.
#
# Build (the context is this folder only; the SDK version comes from global.json):
#   docker build -t spar-linux-ci eng/linux-ci --build-arg DOTNET_SDK_VERSION=$(sed -n 's/.*"version": *"\([^"]*\)".*/\1/p' global.json | head -1)

# Ubuntu 24.04, pinned by digest like the workflow's actions (update it on purpose).
FROM ubuntu:24.04@sha256:9c7f200c07e3d4a03e7249d2c16bf66a51120fb876aa2242cf31451412543289

ARG DEBIAN_FRONTEND=noninteractive

# Packages from Ubuntu's archive, unpinned so that each image build takes the archive's security updates; the layer cache
# keeps a build's packages until this file changes.
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      ca-certificates curl xz-utils \
      libwebkitgtk-6.0-4 libgtk-4-1 bubblewrap xdg-dbus-proxy \
      xvfb xauth x11-xkb-utils xkb-data libxtst6 \
      dbus-daemon dbus-bin dbus-session-bus-common at-spi2-core \
      fontconfig fonts-dejavu-core \
      clang zlib1g-dev \
      iproute2 procps \
 && rm -rf /var/lib/apt/lists/*

# Node, the release line the workflow's setup-node step uses, pinned with its SHA-256 from nodejs.org's SHASUMS256.txt.
ARG NODE_VERSION=24.22.3
ARG NODE_SHA256=4f2b175a6d53a5c617a80dba2676238a0688cef28c6da4eba35c7d844e66fcfb
RUN set -eu \
 && file="node-v${NODE_VERSION}-linux-x64.tar.xz" \
 && curl -fsSL "https://nodejs.org/dist/v${NODE_VERSION}/${file}" -o "/tmp/${file}" \
 && if [ -z "${NODE_SHA256}" ]; then echo "NODE_SHA256 is empty: pin it with the version." >&2; exit 1; fi \
 && echo "${NODE_SHA256}  /tmp/${file}" | sha256sum -c - \
 && mkdir -p /opt/node \
 && tar -xJf "/tmp/${file}" -C /opt/node --strip-components=1 --no-same-owner \
 && rm "/tmp/${file}"

# The .NET SDK global.json names, checked against the SHA-512 in Microsoft's release metadata for its channel.
ARG DOTNET_SDK_VERSION
RUN set -eu \
 && if [ -z "${DOTNET_SDK_VERSION:-}" ]; then echo "Pass --build-arg DOTNET_SDK_VERSION (global.json's sdk.version)." >&2; exit 1; fi \
 && channel=$(echo "${DOTNET_SDK_VERSION}" | cut -d. -f1-2) \
 && url="https://builds.dotnet.microsoft.com/dotnet/Sdk/${DOTNET_SDK_VERSION}/dotnet-sdk-${DOTNET_SDK_VERSION}-linux-x64.tar.gz" \
 && curl -fsSL "https://builds.dotnet.microsoft.com/dotnet/release-metadata/${channel}/releases.json" -o /tmp/releases.json \
 && hash=$(grep -A1 -F "\"url\": \"${url}\"" /tmp/releases.json | sed -n 's/.*"hash": *"\([0-9a-f]*\)".*/\1/p' | head -1) \
 && if [ -z "${hash}" ]; then echo "No SHA-512 for ${url} in the ${channel} release metadata." >&2; exit 1; fi \
 && curl -fsSL "${url}" -o /tmp/dotnet-sdk.tar.gz \
 && echo "${hash}  /tmp/dotnet-sdk.tar.gz" | sha512sum -c - \
 && mkdir -p /opt/dotnet \
 && tar -xzf /tmp/dotnet-sdk.tar.gz -C /opt/dotnet --no-same-owner \
 && rm /tmp/dotnet-sdk.tar.gz /tmp/releases.json \
 && /opt/dotnet/dotnet --list-sdks

ENV DOTNET_ROOT=/opt/dotnet \
    PATH=/opt/dotnet:/opt/node/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_CLI_USE_MSBUILD_SERVER=false

# The black box's Linux host tests (tests/Spar.WindowTests/Linux/): its theme test serves a fake XDG Settings portal on
# a private bus from Python with GObject introspection (python3-gi and what it pulls, 16 packages, 7.0 MB to download),
# and its external-link test asks GIO's `gio mime` which handler opens https before it clicks (libglib2.0-bin, 2
# packages, 148 KB), per apt-get -s on 2026-11-21. A layer of its own, so the layers above stay cached.
RUN apt-get update \
 && apt-get install -y --no-install-recommends python3-gi libglib2.0-bin \
 && rm -rf /var/lib/apt/lists/*

# uid 1000 is Ubuntu's own `ubuntu` user in this base image; Spar's user takes its place.
RUN userdel --remove ubuntu \
 && useradd --create-home --uid 1000 --user-group --shell /bin/bash spar

USER spar
WORKDIR /home/spar
