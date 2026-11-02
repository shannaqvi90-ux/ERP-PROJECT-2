#!/bin/bash
set -u
mkdir -p /work && cp -a /src/. /work/ && cd /work
find . -type d \( -name bin -o -name obj -o -name node_modules \) -prune -exec rm -rf {} + 2>/dev/null
dotnet restore Erp.slnx --verbosity quiet && dotnet build Erp.slnx -c Release --no-restore --verbosity quiet 2>&1 | tail -3
export DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0 DOTNET_GCgen0size=0x4000000 DOTNET_TieredPGO=0 ERP_TEST_PASSWORD_ITERATIONS=10000
dotnet test ${PROJ:-tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj} -c Release --no-build --filter "$FILTER" --logger "console;verbosity=normal" --logger "trx;LogFilePrefix=plant" --results-directory /out/trx 2>&1 | tail -400
