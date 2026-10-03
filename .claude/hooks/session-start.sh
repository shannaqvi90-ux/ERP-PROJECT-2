#!/bin/bash
# Cloud-session setup: Docker daemon and the .NET 10 SDK.
# builds.dotnet.microsoft.com is blocked by egress policy here, so the SDK is copied out of
# Microsoft's official SDK container image instead. Idempotent; does nothing outside cloud sessions.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

if ! docker info >/dev/null 2>&1; then
  (nohup dockerd >/tmp/dockerd.log 2>&1 &)
  for _ in $(seq 1 30); do docker info >/dev/null 2>&1 && break; sleep 1; done
fi

if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  docker pull -q mcr.microsoft.com/dotnet/sdk:10.0 >/dev/null
  cid=$(docker create mcr.microsoft.com/dotnet/sdk:10.0)
  rm -rf /opt/dotnet
  docker cp "$cid":/usr/share/dotnet /opt/dotnet >/dev/null
  docker rm "$cid" >/dev/null
  ln -sf /opt/dotnet/dotnet /usr/local/bin/dotnet
fi

if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo 'export DOTNET_ROOT=/opt/dotnet'
    echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
    echo 'export DOTNET_NOLOGO=1'
  } >> "$CLAUDE_ENV_FILE"
fi
