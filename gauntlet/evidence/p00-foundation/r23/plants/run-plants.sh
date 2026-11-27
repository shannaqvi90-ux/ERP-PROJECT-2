#!/usr/bin/env bash
# Critic p00 r23. Runs inside the toolbox: build the planted copy once, then filtered gate runs per plant.
# Plants (plants.diff) are inert unless CRITIC_PLANT names them.
set -uo pipefail
mkdir -p /work && cd /src && tar -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=.git --exclude=dist . | tar -C /work -xf -
cd /work
echo "build start $(date -u +%T) load $(cut -d' ' -f1 /proc/loadavg)"
dotnet restore Erp.slnx --verbosity quiet && dotnet build Erp.slnx -c Release --no-restore --verbosity quiet -m:4 || { echo BUILD FAILED; exit 1; }
echo "build end $(date -u +%T)"
export ERP_GATE_SHARD_DIR=/tmp/shards; mkdir -p $ERP_GATE_SHARD_DIR
G=tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj
I=tests/Erp.Modules.Identity.Tests/Erp.Modules.Identity.Tests.csproj
run() {
  local name="$1" proj="$2" filter="$3" env="$4"
  echo "=== $name (CRITIC_PLANT=$env) filter $filter start $(date -u +%T) load $(cut -d' ' -f1 /proc/loadavg)"
  CRITIC_PLANT="$env" dotnet test "$proj" -c Release --no-build --verbosity quiet --filter "$filter" --logger "console;verbosity=normal" > "/out/plant-$name.log" 2>&1
  local rc=$?
  echo "=== $name exit $rc end $(date -u +%T)"
  grep -E "^\s*(Failed|Passed|Skipped) [A-Z]|Failed!|Passed!|Total tests" "/out/plant-$name.log" | tail -30
  return $rc
}
for step in ${CRITIC_STEPS:-static listvol g2p23 idp23 g2p23b}; do
  case $step in
    static) run STATIC-L23 "$G" 'FullyQualifiedName~G1ProcessStateTests|FullyQualifiedName~G1OutsideProcessStateTests' L23 ;;
    listvol) run LISTVOL-L23 "$G" 'FullyQualifiedName~G1ListVolumeTests' L23 ;;
    http0) run HTTP0-L23 "$G" 'Shard=0' L23 ;;
    g2p23) run G2-P23 "$G" 'FullyQualifiedName~Erp.Gates.Tests.G2' P23 ;;
    idp23) run ID-P23 "$I" 'FullyQualifiedName~Erp' P23 ;;
    g2p23b) run G2ONE-P23b "$G" 'FullyQualifiedName~G2PermissionTests' P23b ;;
    g2none) run G2ONE-NONE "$G" 'FullyQualifiedName~G2PermissionTests' NONE ;;
  esac
done
echo "all done $(date -u +%T)"
