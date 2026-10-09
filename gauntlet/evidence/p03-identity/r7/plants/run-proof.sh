#!/usr/bin/env bash
# run-proof.sh <source dir> <out dir> <container name> <test filter>: builds the copy and runs one identity test filter.
set -u
src="$1"; out="$2"; name="$3"; filter="$4"; mkdir -p "$out"
docker run --rm --network host -v /var/run/docker.sock:/var/run/docker.sock -v "$src":/src:ro -v "$out":/out \
  -v erp-cache-nuget:/root/.nuget/packages -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -e ERP_TEST_DB_DIRECT=1 -e FILTER="$filter" \
  --name "$name" erp-toolbox:pw1.63.0-net10 bash -c '
set -e; mkdir -p /work; tar -C /src -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=.git . | tar -C /work -xf -
cd /work; dotnet restore tests/Erp.Modules.Identity.Tests/Erp.Modules.Identity.Tests.csproj --verbosity quiet
dotnet build tests/Erp.Modules.Identity.Tests/Erp.Modules.Identity.Tests.csproj -c Release --no-restore --verbosity quiet 2>&1 | tail -5
set +e
dotnet test tests/Erp.Modules.Identity.Tests/Erp.Modules.Identity.Tests.csproj -c Release --no-build --verbosity quiet --filter "$FILTER" --logger "console;verbosity=normal" > /out/proof.log 2>&1; echo "proof rc=$?" >> /out/proof.log
chmod -R a+rwX /out'
