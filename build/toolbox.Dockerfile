# Build-and-test toolbox for ./erp verify: Playwright's image (Node and browsers) plus the .NET 10 SDK.
# A clean machine needs only Docker, bash and git; everything else runs in here.
FROM mcr.microsoft.com/playwright:v1.63.0-noble
COPY --from=mcr.microsoft.com/dotnet/sdk:10.0 /usr/share/dotnet /usr/share/dotnet
RUN ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet
ENV DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    NUGET_XMLDOC_MODE=skip \
    TESTCONTAINERS_RYUK_DISABLED=false
WORKDIR /work
