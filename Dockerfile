# syntax=docker/dockerfile:1
# The one deployable: ASP.NET Core host serving the API and the built single-page app.

FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
# extra_ca: CA certificates for networks that re-terminate TLS (empty on an ordinary machine).
RUN --mount=type=secret,id=extra_ca,target=/run/secrets/extra_ca --mount=type=cache,target=/root/.npm \
    if [ -s /run/secrets/extra_ca ]; then export NODE_EXTRA_CA_CERTS=/run/secrets/extra_ca; fi; \
    npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run check && npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN --mount=type=secret,id=extra_ca,target=/run/secrets/extra_ca --mount=type=cache,target=/root/.nuget/packages \
    if [ -s /run/secrets/extra_ca ]; then cat /run/secrets/extra_ca >>/etc/ssl/certs/ca-certificates.crt; fi; \
    dotnet restore src/Host/Erp.Host/Erp.Host.csproj
RUN --mount=type=cache,target=/root/.nuget/packages dotnet publish src/Host/Erp.Host/Erp.Host.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
COPY --from=web /web/dist ./wwwroot
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    ASPNETCORE_ENVIRONMENT=Production
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Erp.Host.dll"]
