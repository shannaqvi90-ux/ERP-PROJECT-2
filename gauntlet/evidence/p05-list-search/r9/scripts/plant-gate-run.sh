#!/usr/bin/env bash
# Usage: plant-gate-run.sh <source copy> <name> <dotnet test filter>
# Runs the gate tests of a planted source copy inside the product's own toolbox image, as ./erp verify does.
set -u
src="$1"; name="$2"; filter="$3"
out=/home/shan/evidence-staging/p05-list-search/r9
tc=host.docker.internal
docker run --rm --network host --name "c-p05-list-search-r9-plantrun-$name" \
  -v /var/run/docker.sock:/var/run/docker.sock -v "$src":/src:ro \
  -v erp-cache-nuget:/root/.nuget/packages -e TESTCONTAINERS_HOST_OVERRIDE=$tc -e ERP_TEST_DB_DIRECT=1 \
  -e DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0 -e DOTNET_GCgen0size=0x4000000 -e DOTNET_TieredPGO=0 \
  erp-toolbox:pw1.63.0-net10 bash -c "
    mkdir -p /work && tar -C /src -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=dist --exclude=.git . | tar -C /work -xf - && cd /work &&
    dotnet build tests/Erp.Gates.Tests -c Release --verbosity quiet && date -u +%FT%TZ &&
    dotnet test tests/Erp.Gates.Tests -c Release --no-build --filter '$filter' --logger 'console;verbosity=normal'; echo EXIT \$?; date -u +%FT%TZ"
