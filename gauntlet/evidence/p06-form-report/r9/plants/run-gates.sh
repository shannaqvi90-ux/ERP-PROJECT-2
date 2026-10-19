#!/usr/bin/env bash
# Critic p06 r9 (after the r8 critic's runner): builds a planted copy and runs the gate project (and
# optionally other test projects) inside the product's own toolbox image, the image ./erp verify's
# .NET stage uses. usage: run-gates.sh <planted repo> <name> <dotnet test filter> [extra test project...]
set -u
repo="$1"; name="$2"; filter="$3"; shift 3
out=/home/shan/evidence-staging/p06-form-report/r9/plants
extra=""
for p in "$@"; do extra="$extra && dotnet test $p -c Release --no-build --verbosity quiet --logger 'console;verbosity=normal'"; done
start=$(date -u +%FT%TZ)
docker run --rm --network host --name "c-p06-form-report-r9-plant-$name" \
  -v /var/run/docker.sock:/var/run/docker.sock -v "$repo":/src:ro \
  -v erp-cache-nuget:/root/.nuget/packages \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -e ERP_TEST_DB_DIRECT=1 -e ERP_TEST_PASSWORD_ITERATIONS=10000 \
  -e DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0 -e DOTNET_GCgen0size=0x4000000 -e DOTNET_TieredPGO=0 \
  erp-toolbox:pw1.63.0-net10 bash -c "
    mkdir -p /work && tar -C /src -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=.git . | tar -C /work -xf - &&
    cd /work && dotnet restore Erp.slnx --verbosity quiet && dotnet build Erp.slnx -c Release --no-restore --verbosity quiet &&
    { dotnet test tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj -c Release --no-build --verbosity quiet --filter '$filter' --logger 'console;verbosity=normal'; echo \"gates exit \$?\"; true; } $extra
  " > "$out/run-$name.log" 2>&1
rc=$?
echo "start $start end $(date -u +%FT%TZ) exit $rc" >> "$out/run-$name.log"
