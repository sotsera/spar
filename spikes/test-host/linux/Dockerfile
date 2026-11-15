# Linux lane of the test-host spike: Ubuntu 24.04 with Chromium and Xvfb (the Playwright image already on this
# machine), plus the WebKitGTK 6.0 runtime, the NativeAOT toolchain and the repository's .NET SDK.
FROM mcr.microsoft.com/playwright/dotnet:v1.64.1-noble
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update -qq \
 && apt-get install -y --no-install-recommends libwebkitgtk-6.0-4 clang zlib1g-dev procps curl ca-certificates \
 && rm -rf /var/lib/apt/lists/*
RUN curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && bash /tmp/dotnet-install.sh --version 11.0.100 --install-dir /usr/share/dotnet \
 && rm /tmp/dotnet-install.sh \
 && dotnet --list-sdks
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
WORKDIR /work
