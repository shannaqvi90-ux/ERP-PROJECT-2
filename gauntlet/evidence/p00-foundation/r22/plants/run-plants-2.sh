#!/usr/bin/env bash
# Critic p00 r22, second plant run: P22b (handler-level bypass that passes the host's start-up check).
set -uo pipefail
mkdir -p /work && cd /src && tar -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=.git --exclude=dist . | tar -C /work -xf -
cd /work
echo "build start $(date -u +%T) load $(cut -d' ' -f1 /proc/loadavg)"
dotnet restore Erp.slnx --verbosity quiet && dotnet build Erp.slnx -c Release --no-restore --verbosity quiet -m:4 || { echo BUILD FAILED; exit 1; }
echo "build end $(date -u +%T)"
G=tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj
name=G2ONE-Q22; f='FullyQualifiedName~G2PermissionTests.Granting_exactly_one_permission_opens_exactly_the_endpoints_that_declare_it'
echo "=== $name start $(date -u +%T) load $(cut -d' ' -f1 /proc/loadavg)"
CRITIC_PLANT=Q22 dotnet test "$G" -c Release --no-build --verbosity quiet --filter "$f" --logger "console;verbosity=normal" > "/out/plant-$name.log" 2>&1
echo "=== $name exit $? end $(date -u +%T)"
grep -E "^\s*(Failed|Passed) [A-Z]|Total tests" "/out/plant-$name.log" | tail -10
