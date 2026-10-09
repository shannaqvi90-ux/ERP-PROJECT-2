#!/usr/bin/env bash
# Runs the gate project (or a filtered part) of a planted copy inside the product's toolbox image.
# usage: run-gates.sh <planted repo> <name> <dotnet test filter>
set -u
repo="$1"; name="$2"; filter="$3"
out=/home/shan/evidence-staging/p06-form-report/r4/plants
docker run --rm --network host --name "c-p06-form-report-r4-plant-$name" \
  -v /var/run/docker.sock:/var/run/docker.sock -v "$repo":/src:ro \
  -v erp-cache-nuget:/root/.nuget/packages \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -e ERP_TEST_DB_DIRECT=1 \
  -e DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0 -e DOTNET_GCgen0size=0x4000000 \
  erp-toolbox:pw1.63.0-net10 bash -c "
    mkdir -p /work && tar -C /src -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=.git . | tar -C /work -xf - &&
    cd /work && dotnet restore Erp.slnx --verbosity quiet && dotnet build Erp.slnx -c Release --no-restore --verbosity quiet &&
    dotnet test tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj -c Release --no-build --verbosity quiet --filter '$filter' --logger 'console;verbosity=normal'
  " > "$out/run-$name.log" 2>&1
echo "exit $?" >> "$out/run-$name.log"
