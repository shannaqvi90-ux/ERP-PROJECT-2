#!/usr/bin/env bash
# Critic p03 r10: the dotnet stage of ./erp verify (build plus every .NET test) on the planted copy.
# Run through gauntlet/tools/verify-slot.sh. Same container settings as ./erp's run_toolbox.
set -u
SRC=/home/shan/critic/p03-identity-r10-plants
OUT=/home/shan/evidence-staging/p03-identity/r10/plants/run1
docker volume rm c-p03-identity-r10-plants-work >/dev/null 2>&1 || true
docker run --rm --network host --name c-p03-identity-r10-plants-dotnet \
  -v /var/run/docker.sock:/var/run/docker.sock -v "$SRC":/src:ro -v "$OUT":/out \
  -v erp-cache-nuget:/root/.nuget/packages -v erp-cache-npm:/root/.npm \
  -v c-p03-identity-r10-plants-work:/work \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -e ERP_TEST_DB_DIRECT=1 \
  erp-toolbox:pw1.63.0-net10 bash /src/build/verify-inside.sh dotnet
rc=$?
docker volume rm c-p03-identity-r10-plants-work >/dev/null 2>&1 || true
echo "planted dotnet stage exit $rc"
exit $rc
