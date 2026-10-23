#!/usr/bin/env bash
# Critic p04 r13: build a planted source tree (one diff on b4db2fd7) in the product's toolbox image and run a filtered set of gate tests.
# Usage: run-plant.sh <name> <diff> <dotnet test filter>
set -u
name="$1"; diff="$2"; filter="$3"
src=/home/shan/critic/p04-shell-r13-plant-$name
out=/home/shan/evidence-staging/p04-shell/r13/plants/out-$name
rm -rf "$src"; git clone -q /home/shan/critic/p04-shell-r13 "$src" && cd "$src" && git checkout -q b4db2fd7 && git apply "$diff" || exit 9
mkdir -p "$out"
docker run --rm --network host --name c-p04-shell-r13-plant-$name \
  -v /var/run/docker.sock:/var/run/docker.sock -v "$src":/src:ro -v "$out":/out \
  -v c-p04-shell-r13-plant-$name-work:/work -v erp-cache-nuget:/root/.nuget/packages \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -e ERP_TEST_DB_DIRECT=1 \
  -e DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0 -e DOTNET_GCgen0size=0x4000000 -e DOTNET_TieredPGO=0 -e ERP_TEST_PASSWORD_ITERATIONS=10000 \
  erp-toolbox:pw1.63.0-net10 bash -c "
    set -e
    tar -C /src -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=dist --exclude=.git . | tar -C /work -xf -
    cd /work && dotnet restore Erp.slnx --verbosity quiet && dotnet build Erp.slnx -c Release --no-restore --verbosity quiet
    dotnet test tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj -c Release --no-build --filter '$filter' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=$name.trx' --results-directory /out
  "
rc=$?
docker volume rm c-p04-shell-r13-plant-$name-work >/dev/null 2>&1
echo "exit $rc"
