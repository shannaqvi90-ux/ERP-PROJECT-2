#!/usr/bin/env bash
# Runs the .NET gate suite (and identity module tests) of a planted copy in the product's own toolbox image,
# the way ./erp verify's dotnet stage does (gate self-test processes and timing excluded).
#   run-gates.sh <source dir> <out dir> [extra dotnet test filter]
set -u
src="$1"; out="$2"; mkdir -p "$out"
tc=host.docker.internal
docker run --rm --network host -v /var/run/docker.sock:/var/run/docker.sock -v "$src":/src:ro -v "$out":/out \
  -v erp-cache-nuget:/root/.nuget/packages -e TESTCONTAINERS_HOST_OVERRIDE=$tc -e ERP_TEST_DB_DIRECT=1 \
  --name "c-p03-identity-r6-plants-$(basename "$out")" erp-toolbox:pw1.63.0-net10 bash -c '
set -e; mkdir -p /work; tar -C /src -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=.git . | tar -C /work -xf -
cd /work; dotnet restore Erp.slnx --verbosity quiet; dotnet build Erp.slnx -c Release --no-restore --verbosity quiet 2>&1 | tail -20
export DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0 DOTNET_GCgen0size=0x4000000
set +e
dotnet test tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj -c Release --no-build --verbosity quiet --filter "Load!=Timing&Process!=self-http&Process!=self-company&Process!=self-noninterference" --logger "console;verbosity=normal" > /out/gates.log 2>&1; echo "gates rc=$?" >> /out/gates.log
dotnet test tests/Erp.Modules.Identity.Tests/Erp.Modules.Identity.Tests.csproj -c Release --no-build --verbosity quiet --filter "Load!=Timing" --logger "console;verbosity=normal" > /out/identity.log 2>&1; echo "identity rc=$?" >> /out/identity.log
'
echo "done $(date -u +%FT%TZ)" >> "$out/done"
