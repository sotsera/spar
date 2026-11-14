# Linux desktop spike: the loopback spike's image (Ubuntu 24.04, .NET 11 SDK, Xvfb, GTK3/GTK4 runtime) plus WebKitGTK,
# the NativeAOT linker and a few probes. Each layer logs apt's own size summary under /opt/spar-apt/.
FROM spar-linux-loopback:sdk11.0.100
ENV DEBIAN_FRONTEND=noninteractive
RUN mkdir -p /opt/spar-apt && apt-get update -qq

# 1. What an end user needs to run the exe: the WebKitGTK runtimes only (GTK3 API 4.1 and GTK4 API 6.0).
RUN apt-get install -y --no-install-recommends libwebkit2gtk-4.1-0 2>&1 | tee /opt/spar-apt/1-runtime-4.1.log | grep -E "^(Need|After)" \
 && apt-get install -y --no-install-recommends libwebkitgtk-6.0-4 2>&1 | tee /opt/spar-apt/2-runtime-6.0.log | grep -E "^(Need|After)"

# 2. Headers (to check declarations), the WebDriver, and the NativeAOT toolchain.
RUN apt-get install -y --no-install-recommends libwebkit2gtk-4.1-dev libwebkitgtk-6.0-dev 2>&1 | tee /opt/spar-apt/3-dev.log | grep -E "^(Need|After)" \
 && apt-get install -y --no-install-recommends webkit2gtk-driver 2>&1 | tee /opt/spar-apt/4-driver.log | grep -E "^(Need|After)" \
 && apt-get install -y --no-install-recommends clang zlib1g-dev 2>&1 | tee /opt/spar-apt/5-clang.log | grep -E "^(Need|After)" \
 && apt-get install -y --no-install-recommends xdotool procps psmisc 2>&1 | tee /opt/spar-apt/6-probes.log | grep -E "^(Need|After)" \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /work
