#!/usr/bin/env bash
# Runs inside the toolbox: build the planted clone once, then the gate tests per plant.
set -uo pipefail
mkdir -p /work && cd /src && tar -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=.git . | tar -C /work -xf -
cd /work
dotnet restore Erp.slnx --verbosity quiet && dotnet build Erp.slnx -c Release --no-restore --verbosity quiet || { echo BUILD FAILED; exit 1; }
export DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0
base='Load!=Timing&Process!=self-http&Process!=self-company&Process!=self-noninterference'
run() {
  local plant="$1" filter="$2"
  echo "=== plant $plant filter $filter start $(date -u +%T)"
  CRITIC_PLANT="$plant" dotnet test tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj -c Release --no-build --verbosity quiet \
    --filter "($filter)&$base" --logger "console;verbosity=normal" > "/out/plant-$plant.log" 2>&1
  echo "=== plant $plant exit $? end $(date -u +%T)"
  grep -E "^\s*(Failed|Passed|Total|Skipped) |Failed!|Passed!|failed:|Total tests" "/out/plant-$plant.log" | tail -15
}
for spec in ${PLANTS:-P1 T3 T2 T1}; do
  case "$spec" in
    P1) run P1 'FullyQualifiedName~G2PermissionTests' ;;
    T3) run T3 'FullyQualifiedName~G1DatabaseIsolationTests|FullyQualifiedName~G1HttpIsolationTests' ;;
    T2) run T2 'FullyQualifiedName~G1TenantSettingCodeTests|FullyQualifiedName~G1TenantValueRuleTests|FullyQualifiedName~G1TenantSourceTests|FullyQualifiedName~G1HttpIsolationTests' ;;
    T1) run T1 'FullyQualifiedName~G1HttpIsolationTests|FullyQualifiedName~G1ProcessStateTests|FullyQualifiedName~G1NonInterferenceTests' ;;
  esac
done
