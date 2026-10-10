#!/usr/bin/env bash
# Critic p03 r9: runs the whole .NET suite of a planted copy (every project, gate self-test processes and
# timing excluded) in the product's own toolbox image, as ./erp verify's dotnet stage does.
#   run-gates.sh <source dir> <out dir> <container name>
set -u
src="$1"; out="$2"; name="$3"; mkdir -p "$out"
docker run --rm --network host -v /var/run/docker.sock:/var/run/docker.sock -v "$src":/src:ro -v "$out":/out \
  -v erp-cache-nuget:/root/.nuget/packages -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -e ERP_TEST_DB_DIRECT=1 \
  --name "$name" erp-toolbox:pw1.63.0-net10 bash -c '
set -e; mkdir -p /work; tar -C /src -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=.git . | tar -C /work -xf -
cd /work; dotnet restore Erp.slnx --verbosity quiet; dotnet build Erp.slnx -c Release --no-restore --verbosity quiet > /out/build.log 2>&1 || { echo "build failed" >> /out/build.log; chmod -R a+rwX /out; exit 3; }
export DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0 DOTNET_GCgen0size=0x4000000 DOTNET_TieredPGO=0
set +e
dotnet test Erp.slnx -c Release --no-build --verbosity quiet --filter "Load!=Timing&Process!=self-http&Process!=self-company&Process!=self-noninterference" --logger "console;verbosity=normal" > /out/suite.log 2>&1; echo "suite rc=$?" >> /out/suite.log
chmod -R a+rwX /out
'
echo "done $(date -u +%FT%TZ)" >> "$out/done"
