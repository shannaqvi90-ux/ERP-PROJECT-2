#!/usr/bin/env bash
# Critic p00 r20. Runs inside the toolbox: build the planted copy once, then gate tests per plant.
# Plants (plants.diff) are inert unless CRITIC_PLANT names them.
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
    STATICL20) run STATICL20 'FullyQualifiedName~G1ProcessStateTests|FullyQualifiedName~G1OutsideProcessStateTests|FullyQualifiedName~G1TenantSourceTests' L20 ;;
    NIL20) run NIL20 'FullyQualifiedName~G1NonInterferenceTests&Load!=Timing' L20 ;;
    SHARD0L20) run SHARD0L20 'Shard=0' L20 ;;
    SHARD0NONE) run SHARD0NONE 'Shard=0' NONE ;;
    G2P20) run G2P20 '(FullyQualifiedName~Erp.Gates.Tests.G2)&Load!=Timing' P20 ;;
    LISTTESTS)
      # The partition check of build/verify-inside.sh, its two listings, run again and again on the unplanted
      # build (the plants change no test): a line naming two tests means the parallel listing interleaved.
      for n in $(seq 1 ${LIST_RUNS:-12}); do
        { dotnet test Erp.slnx -c Release --no-build --verbosity quiet --list-tests --filter "Load!=Timing" 2>&1 || true; } | { grep '^    ' || true; } | sort > /out/list-all-$n.txt
        merged=$(grep -cE '^    [^ ].*[^ ]    [^ ]' /out/list-all-$n.txt || true)
        echo "listing $n: $(wc -l < /out/list-all-$n.txt) lines, $merged lines naming two tests $(date -u +%T)"
        grep -E '^    [^ ].*[^ ]    [^ ]' /out/list-all-$n.txt | head -3
      done ;;
    G1COP20) run G1COP20 'FullyQualifiedName~G1CompanyIsolationTests|FullyQualifiedName~G1CompanyScopeTests' P20 ;;
  esac
done
