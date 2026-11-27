#!/usr/bin/env bash
# Critic p00 r24. Runs inside the toolbox: build the planted copy once, then filtered gate runs per plant.
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
  local name="$1" proj="$2" filter="$3" env="$4"
  echo "=== $name (CRITIC_PLANT=$env) filter $filter start $(date -u +%T) load $(cut -d' ' -f1 /proc/loadavg)"
  CRITIC_PLANT="$env" dotnet test "$proj" -c Release --no-build --verbosity quiet --filter "$filter" --logger "console;verbosity=normal" > "/out/plant-$name.log" 2>&1
  local rc=$?
  echo "=== $name exit $rc end $(date -u +%T)"
  grep -E "^\s*(Failed|Passed|Skipped) [A-Z]|Failed!|Passed!|Total tests" "/out/plant-$name.log" | tail -30
  return $rc
}
for step in ${CRITIC_STEPS:-s24 s24none static listvol p24 p24none}; do
  case $step in
    s24) run SHARDS-S24 "$G" 'FullyQualifiedName~HttpAttackShardTests' S24 ;;
    s24none) run SHARDS-NONE "$G" 'FullyQualifiedName~HttpAttackShardTests' NONE ;;
    static) run STATIC-T24 "$G" 'FullyQualifiedName~G1ProcessStateTests|FullyQualifiedName~G1OutsideProcessStateTests|FullyQualifiedName~G1TenantSourceTests' T24 ;;
    listvol) run LISTVOL-T24 "$G" 'FullyQualifiedName~G1ListVolumeTests' T24 ;;
    http) run HTTP-T24 "$G" 'FullyQualifiedName~G1HttpIsolationShardTests&FullyQualifiedName~first' T24 ;;
    p24) run G2-P24 "$G" 'FullyQualifiedName~Erp.Gates.Tests.G2' P24 ;;
    p24b) run G2-P24b "$G" 'FullyQualifiedName~Erp.Gates.Tests.G2' P24b ; run JOBS-P24b tests/Erp.Modules.Jobs.Tests/Erp.Modules.Jobs.Tests.csproj 'FullyQualifiedName~Erp' P24b ;;
    p24c) run G2-P24c "$G" 'FullyQualifiedName~Erp.Gates.Tests.G2' P24c ; run AUDIT-P24c tests/Erp.Modules.Audit.Tests/Erp.Modules.Audit.Tests.csproj 'FullyQualifiedName~Erp' P24c ; run AUDITGATE-P24c "$G" 'FullyQualifiedName~Audit' P24c ;;
    probe) run PROBE-P24c tests/Erp.Modules.Audit.Tests/Erp.Modules.Audit.Tests.csproj 'FullyQualifiedName~CRITIC_PROBE' P24c ; run PROBE-NONE tests/Erp.Modules.Audit.Tests/Erp.Modules.Audit.Tests.csproj 'FullyQualifiedName~CRITIC_PROBE' NONE ;;
    p24none) run G2-NONE "$G" 'FullyQualifiedName~Erp.Gates.Tests.G2' NONE ;;
  esac
done
echo "all done $(date -u +%T)"
