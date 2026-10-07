#!/usr/bin/env bash
# usage: run-gates.sh <src dir> <out dir> <name> [filter]
set -u
SRC="$1"; OUT="$2"; NAME="$3"; FILTER="${4:-Load!=Timing&Process!=self-http&Process!=self-company&Process!=self-noninterference}"
mkdir -p "$OUT"
docker run --rm --network host --name "c-p06-form-report-r2-plant-$NAME" \
  -v /var/run/docker.sock:/var/run/docker.sock -v "$SRC":/src:ro -v "$OUT":/out \
  -v erp-cache-nuget:/root/.nuget/packages -v erp-cache-npm:/root/.npm \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -e ERP_TEST_DB_DIRECT=1 \
  -e DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0 -e DOTNET_GCgen0size=0x4000000 \
  -e FILTER="$FILTER" -e NAME="$NAME" \
  erp-toolbox:pw1.63.0-net10 bash -c '
    mkdir -p /work && cd /src && tar --exclude=./web/node_modules --exclude=./gauntlet/compare/node_modules --exclude=./.git --exclude=bin --exclude=obj -cf - . | (cd /work && tar xf -)
    cd /work && dotnet restore Erp.slnx --verbosity quiet && dotnet build tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj -c Release --no-restore --verbosity quiet || exit 3
    dotnet test tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj -c Release --no-build --verbosity quiet \
      --filter "$FILTER" --logger "console;verbosity=normal" --logger "trx;LogFileName=gates-$NAME.trx" --results-directory /out' > "$OUT/run-$NAME.log" 2>&1
echo "exit $?" >> "$OUT/run-$NAME.log"
