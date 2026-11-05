#!/usr/bin/env bash
# Critic p00 r17: the verify's one failing test, run on its own (unplanted clone of 417f1990), to characterise it.
set -uo pipefail
mkdir -p /work && cd /src && tar -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=.git --exclude=dist --exclude=.verify-failed . | tar -C /work -xf -
cd /work
dotnet restore Erp.slnx --verbosity quiet && dotnet build Erp.slnx -c Release --no-restore --verbosity quiet -m:4 || { echo BUILD FAILED; exit 1; }
for i in 1 2; do
  echo "=== run $i start $(date -u +%T)"
  dotnet test tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj -c Release --no-build --verbosity quiet \
    --filter "FullyQualifiedName~G1CompanyIsolationTests&Load!=Timing" --logger "console;verbosity=normal" > "/out/rerun-$i.log" 2>&1
  echo "=== run $i exit $? end $(date -u +%T)"
  grep -E "^\s*(Failed|Passed) |Total tests" "/out/rerun-$i.log" | tail -10
done
