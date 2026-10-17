#!/usr/bin/env bash
# Critic p04 r9: build a planted source tree in the product's toolbox image and run a filtered set of gate tests.
# Usage: run-plant.sh <B1|B2> <dotnet test filter>
set -u
b="$1"; filter="$2"
src=/home/shan/critic/p04-shell-r9-$b
out=/home/shan/evidence-staging/p04-shell/r9/plants/out-$b
mkdir -p "$out"
docker run --rm --network host --name c-p04-shell-r9-plant-$b \
  -v /var/run/docker.sock:/var/run/docker.sock -v "$src":/src:ro -v "$out":/out \
  -v c-p04-shell-r9-plant-$b-work:/work -v erp-cache-nuget:/root/.nuget/packages \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -e ERP_TEST_DB_DIRECT=1 \
  -e DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0 -e DOTNET_GCgen0size=0x4000000 -e DOTNET_TieredPGO=0 -e ERP_TEST_PASSWORD_ITERATIONS=10000 \
  erp-toolbox:pw1.63.0-net10 bash -c "
    set -e
    tar -C /src -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=dist --exclude=.git . | tar -C /work -xf -
    cd /work && dotnet restore Erp.slnx --verbosity quiet && dotnet build Erp.slnx -c Release --no-restore --verbosity quiet
    dotnet test tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj -c Release --no-build --filter '$filter' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=$b.trx' --results-directory /out
  "
echo "exit $?"
