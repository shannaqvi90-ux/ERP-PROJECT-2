#!/usr/bin/env bash
# Critic p03 r9: builds a copy with plants/proof/CriticR9Proof.cs added to the identity tests and runs that one test.
#   run-proof.sh <source dir> <out dir> <container name>
set -u
src="$1"; out="$2"; name="$3"; mkdir -p "$out"; proof="$(cd "$(dirname "$0")" && pwd)/proof"
docker run --rm --network host -v /var/run/docker.sock:/var/run/docker.sock -v "$src":/src:ro -v "$proof":/proof:ro -v "$out":/out \
  -v erp-cache-nuget:/root/.nuget/packages -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -e ERP_TEST_DB_DIRECT=1 \
  --name "$name" erp-toolbox:pw1.63.0-net10 bash -c '
set -e; mkdir -p /work; tar -C /src -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=.git . | tar -C /work -xf -
cp /proof/CriticR9Proof.cs /work/tests/Erp.Modules.Identity.Tests/
cd /work; dotnet build tests/Erp.Modules.Identity.Tests/Erp.Modules.Identity.Tests.csproj -c Release --verbosity quiet > /out/build.log 2>&1 || { tail -30 /out/build.log; exit 3; }
set +e
dotnet test tests/Erp.Modules.Identity.Tests/Erp.Modules.Identity.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~CriticR9Proof" --logger "console;verbosity=detailed" > /out/proof.log 2>&1; echo "proof rc=$?" >> /out/proof.log
chmod -R a+rwX /out
'
