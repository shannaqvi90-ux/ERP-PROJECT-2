#!/usr/bin/env bash
# Critic p00 r21. Runs inside the toolbox: build the planted copy once, then small filtered gate runs per plant.
# Plants (plants.diff) are inert unless CRITIC_PLANT names them (the L21 field exists in the build either way).
set -uo pipefail
mkdir -p /work && cd /src && tar -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=.git --exclude=dist . | tar -C /work -xf -
cd /work
echo "build start $(date -u +%T)"
dotnet restore Erp.slnx --verbosity quiet && dotnet build Erp.slnx -c Release --no-restore --verbosity quiet -m:4 || { echo BUILD FAILED; exit 1; }
echo "build end $(date -u +%T)"
export ERP_GATE_SHARD_DIR=/tmp/shards; mkdir -p $ERP_GATE_SHARD_DIR
run() {
  local name="$1" filter="$2" env="$3" project="${4:-tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj}"
  echo "=== $name (CRITIC_PLANT=$env) filter $filter start $(date -u +%T)"
  CRITIC_PLANT="$env" dotnet test "$project" -c Release --no-build --verbosity quiet \
    --filter "$filter" --logger "console;verbosity=normal" > "/out/plant-$name.log" 2>&1
  echo "=== $name exit $? end $(date -u +%T)"
  grep -E "^\s*(Failed|Total|Skipped) |Failed!|Passed!|Total tests" "/out/plant-$name.log" | tail -40
}
for spec in ${PLANTS}; do
  case "$spec" in
    STATICL21) run STATICL21 'FullyQualifiedName~G1ProcessStateTests|FullyQualifiedName~G1OutsideProcessStateTests|FullyQualifiedName~G1TenantSourceTests' L21 ;;
    G2ONEP21) run G2ONEP21 'FullyQualifiedName~G2PermissionTests.Granting_exactly_one_permission_opens_exactly_the_endpoints_that_declare_it' P21 ;;
    G2ONENONE) run G2ONENONE 'FullyQualifiedName~G2PermissionTests.Granting_exactly_one_permission_opens_exactly_the_endpoints_that_declare_it' NONE ;;
  esac
done
# LISTTESTS21: the round-21 partition listing (build/list-tests.sh, one project at a time, strict parser), run again and again
# on the same build under the machine's current load: every listing must be identical and parse.
if [[ " ${PLANTS} " == *" LISTTESTS21 "* ]]; then
  source /work/build/list-tests.sh
  for n in $(seq 1 ${LIST_RUNS:-6}); do
    if list_tests Erp.slnx --filter "Load!=Timing" > /out/list21-$n.txt 2>/out/list21-$n.err; then rc=0; else rc=$?; fi
    echo "listing $n: rc $rc, $(wc -l < /out/list21-$n.txt) names, md5 $(md5sum < /out/list21-$n.txt | cut -c1-12), load $(cut -d' ' -f1 /proc/loadavg) $(date -u +%T)"
  done
fi
