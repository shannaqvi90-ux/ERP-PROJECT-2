#!/usr/bin/env bash
# Critic p00 r9. Runs inside the toolbox: build the planted copy once, then the gate suite per plant.
# Plants (plants.diff) are inert unless CRITIC_PLANT names them.
set -uo pipefail
mkdir -p /work && cd /src && tar -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=.git . | tar -C /work -xf -
cd /work
dotnet restore Erp.slnx --verbosity quiet && dotnet build Erp.slnx -c Release --no-restore --verbosity quiet || { echo BUILD FAILED; exit 1; }
base='Load!=Timing'
run() {
  local plant="$1" filter="$2" env="${3:-$1}"
  echo "=== plant $plant filter $filter start $(date -u +%T)"
  CRITIC_PLANT="$env" dotnet test tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj -c Release --no-build --verbosity quiet \
    --filter "($filter)&$base" --logger "console;verbosity=normal" > "/out/plant-$plant.log" 2>&1
  echo "=== plant $plant exit $? end $(date -u +%T)"
  grep -E "^\s*(Failed|Passed|Total|Skipped) |Failed!|Passed!|failed:|Total tests" "/out/plant-$plant.log" | tail -25
}
for spec in ${PLANTS:-P3 L4}; do
  case "$spec" in
    P3) run P3 'FullyQualifiedName~Erp.Gates.Tests.G2' ;;
    P3G1) run P3G1 'FullyQualifiedName~Erp.Gates.Tests.G1|FullyQualifiedName~Erp.Gates.Tests.Rules' P3 ;;
    P3M) echo "=== plant P3M (module tests) start $(date -u +%T)"
         for proj in Erp.Modules.Identity.Tests Erp.Modules.Tenancy.Tests Erp.Modules.Lists.Tests Erp.Modules.Reports.Tests Erp.Kernel.Tests; do
           CRITIC_PLANT=P3 dotnet test tests/$proj/$proj.csproj -c Release --no-build --verbosity quiet --filter 'Load!=Timing' --logger "console;verbosity=normal" > "/out/plant-P3M-$proj.log" 2>&1
           echo "--- $proj exit $?"; grep -E "^\s*Failed |Total tests|Passed!|Failed!" "/out/plant-P3M-$proj.log" | tail -12
         done ;;
    P3PROOF) run P3PROOF 'FullyQualifiedName~CriticP3Proof' P3 ;;
    P3NONE) run P3NONE 'FullyQualifiedName~CriticP3Proof' NONE ;;
    L4) run L4 'FullyQualifiedName~Erp.Gates.Tests.G1' ;;
  esac
done
# (appended) P3 proof: the critic's own test shows the plant is live, with and without the plant.
