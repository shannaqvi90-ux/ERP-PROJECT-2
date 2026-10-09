#!/usr/bin/env bash
# Runs inside the toolbox: build the planted clone once, then the gate tests per plant.
# Plants (plants.diff) are inert unless CRITIC_PLANT names them.
set -uo pipefail
mkdir -p /work && cd /src && tar -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=.git . | tar -C /work -xf -
cd /work
dotnet restore Erp.slnx --verbosity quiet && dotnet build Erp.slnx -c Release --no-restore --verbosity quiet || { echo BUILD FAILED; exit 1; }
base='Load!=Timing&Process!=self-http&Process!=self-company&Process!=self-noninterference'
run() {
  local plant="$1" filter="$2" env="${3:-$1}"
  echo "=== plant $plant filter $filter start $(date -u +%T)"
  CRITIC_PLANT="$env" dotnet test tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj -c Release --no-build --verbosity quiet \
    --filter "($filter)&$base" --logger "console;verbosity=normal" > "/out/plant-$plant.log" 2>&1
  echo "=== plant $plant exit $? end $(date -u +%T)"
  grep -E "^\s*(Failed|Passed|Total|Skipped) |Failed!|Passed!|failed:|Total tests" "/out/plant-$plant.log" | tail -15
}
for spec in ${PLANTS:-L1 P1 P2 L3}; do
  case "$spec" in
    L1) run L1 'FullyQualifiedName~G1RowVersionTests' ;;
    P1) run P1 'FullyQualifiedName~G2PermissionTests' ;;
    P2) run P2 'FullyQualifiedName~G2PermissionTests' ;;
    L3) run L3 'FullyQualifiedName~G1NonInterferenceTests' ;;
    L3H) run L3H 'FullyQualifiedName~G1HttpIsolationTests' L3 ;;
    L3G) run L3G 'FullyQualifiedName~Erp.Gates.Tests' L3 ;;
    L3M) echo "=== plant L3M (module and kernel tests) start $(date -u +%T)"
         for proj in Erp.Kernel.Tests Erp.Modules.Identity.Tests Erp.Modules.Tenancy.Tests Erp.Modules.Lists.Tests Erp.Modules.Reports.Tests; do
           CRITIC_PLANT=L3 dotnet test tests/$proj/$proj.csproj -c Release --no-build --verbosity quiet --filter 'Load!=Timing' --logger "console;verbosity=normal" > "/out/plant-L3M-$proj.log" 2>&1
           echo "--- $proj exit $?"; grep -E "^\s*Failed |Total tests|Passed!|Failed!" "/out/plant-L3M-$proj.log" | tail -12
         done ;;
    NONE) run NONE 'FullyQualifiedName~G1RowVersionTests' ;;
  esac
done
