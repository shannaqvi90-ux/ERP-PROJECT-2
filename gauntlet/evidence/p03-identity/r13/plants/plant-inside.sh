#!/usr/bin/env bash
# Critic p03 r13: the planted clone's .NET gate suite (G1, G2 and rule gates) and the identity tests,
# in the toolbox container, the same settings as ./erp verify's dotnet stage. Self-test processes excluded
# (they plant their own modules); timing budgets excluded.
set -uo pipefail
tar -C /src -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=dist --exclude=test-results --exclude=playwright-report --exclude=.git . | tar -C /work -xf -
cd /work
dotnet restore Erp.slnx --verbosity quiet && dotnet build Erp.slnx -c Release --no-restore --verbosity quiet || { echo BUILD FAILED; exit 3; }
export DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0 DOTNET_GCgen0size=0x4000000 DOTNET_TieredPGO=0 ERP_TEST_PASSWORD_ITERATIONS=10000
f="Load!=Timing&Process!=self-http&Process!=self-company&Process!=self-noninterference"
dotnet test tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj -c Release --no-build --verbosity quiet --filter "$f" --logger "trx;LogFilePrefix=gates" --logger "console;verbosity=normal" --results-directory /out/trx > /out/gates.log 2>&1 & g=$!
dotnet test tests/Erp.Modules.Identity.Tests/Erp.Modules.Identity.Tests.csproj -c Release --no-build --verbosity quiet --filter "$f" --logger "trx;LogFilePrefix=identity" --logger "console;verbosity=normal" --results-directory /out/trx > /out/identity.log 2>&1 & i=$!
wait $g; echo "gates exit $?"; wait $i; echo "identity exit $?"
