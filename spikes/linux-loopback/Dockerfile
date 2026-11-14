# Playwright for .NET 1.64.1 image (Ubuntu 24.04, browsers and their system deps preinstalled) plus the pinned .NET 11 SDK.
FROM mcr.microsoft.com/playwright/dotnet:v1.64.1-noble
ADD https://dot.net/v1/dotnet-install.sh /tmp/dotnet-install.sh
RUN chmod +x /tmp/dotnet-install.sh \
 && /tmp/dotnet-install.sh --version 11.0.100 --install-dir /opt/dotnet11 \
 && rm /tmp/dotnet-install.sh
ENV DOTNET_ROOT=/opt/dotnet11 \
    PATH=/opt/dotnet11:$PATH \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
WORKDIR /work
