#!/usr/bin/env bash
# Critic p00 r22. Runs inside the toolbox: build the planted copy once, then small filtered gate runs per plant.
# Plants (plants.diff) are inert unless CRITIC_PLANT names them.
set -uo pipefail
mkdir -p /work && cd /src && tar -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=.git --exclude=dist . | tar -C /work -xf -
cd /work
echo "build start $(date -u +%T) load $(cut -d' ' -f1 /proc/loadavg)"
dotnet restore Erp.slnx --verbosity quiet && dotnet build Erp.slnx -c Release --no-restore --verbosity quiet -m:4 || { echo BUILD FAILED; exit 1; }
echo "build end $(date -u +%T)"
export ERP_GATE_SHARD_DIR=/tmp/shards; mkdir -p $ERP_GATE_SHARD_DIR
G=tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj
run() {
  local name="$1" filter="$2" env="$3"
  echo "=== $name (CRITIC_PLANT=$env) filter $filter start $(date -u +%T) load $(cut -d' ' -f1 /proc/loadavg)"
  CRITIC_PLANT="$env" dotnet test "$G" -c Release --no-build --verbosity quiet --filter "$filter" --logger "console;verbosity=normal" > "/out/plant-$name.log" 2>&1
  local rc=$?
  echo "=== $name exit $rc end $(date -u +%T)"
  grep -E "^\s*(Failed|Passed|Skipped) [A-Z]|Failed!|Passed!|Total tests" "/out/plant-$name.log" | tail -30
  return $rc
}
caught=0
run STATIC-L22 'FullyQualifiedName~G1ProcessStateTests|FullyQualifiedName~G1OutsideProcessStateTests' L22 || caught=1
run LISTVOL-L22 'FullyQualifiedName~G1ListVolumeTests' L22 || caught=1
if [[ $caught == 0 ]]; then run HTTP0-L22 'Shard=0' L22; fi
run G2-P22 'FullyQualifiedName~G2PermissionTests' P22
run G2-NONE 'FullyQualifiedName~G2PermissionTests' NONE
echo "all done $(date -u +%T)"
