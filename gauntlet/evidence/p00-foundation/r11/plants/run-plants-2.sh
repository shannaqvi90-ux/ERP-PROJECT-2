#!/usr/bin/env bash
# Critic p00 r11. Runs inside the toolbox: build the planted copy once, then gate suites per plant.
# Plants (plants.diff) are inert unless CRITIC_PLANT names them.
set -uo pipefail
mkdir -p /work && cd /src && tar -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=.git --exclude=dist . | tar -C /work -xf -
cd /work
dotnet restore Erp.slnx --verbosity quiet && dotnet build Erp.slnx -c Release --no-restore --verbosity quiet || { echo BUILD FAILED; exit 1; }
base='Load!=Timing'
run() {
  local name="$1" filter="$2" env="$3"
  echo "=== $name (CRITIC_PLANT=$env) filter $filter start $(date -u +%T)"
  CRITIC_PLANT="$env" dotnet test tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj -c Release --no-build --verbosity quiet \
    --filter "($filter)&$base" --logger "console;verbosity=normal" > "/out/plant-$name.log" 2>&1
  echo "=== $name exit $? end $(date -u +%T)"
  grep -E "^\s*(Failed|Total|Skipped) |Failed!|Passed!|Total tests" "/out/plant-$name.log" | tail -30
}
for spec in ${PLANTS:-P6ESC P6ESCNONE P6G1 P6M}; do
  case "$spec" in
    P6G1) run P6G1 'FullyQualifiedName~Erp.Gates.Tests.G1|FullyQualifiedName~Erp.Gates.Tests.SelfTests' P6 ;;
    P6M) echo "=== P6M (CRITIC_PLANT=P6) kernel and module test projects start $(date -u +%T)"
         for proj in tests/Erp.Kernel.Tests tests/Erp.Modules.*.Tests; do
           CRITIC_PLANT=P6 dotnet test "$proj" -c Release --no-build --verbosity quiet --filter "$base" --logger "console;verbosity=normal" >> /out/plant-P6M.log 2>&1
           echo "    $proj exit $?"
         done
         grep -E "^\s*Failed |Total tests|Failed!|Passed!" /out/plant-P6M.log | tail -30 ;;
    P6ESC) run P6ESC 'FullyQualifiedName~CriticR11Escalation' P6 ;;
    P6ESCNONE) run P6ESCNONE 'FullyQualifiedName~CriticR11Escalation' NONE ;;
    P6PROOF) run P6PROOF 'FullyQualifiedName~CriticR11Proof' P6 ;;
    P6NONE) run P6NONE 'FullyQualifiedName~CriticR11Proof' NONE ;;
    P6) run P6 'FullyQualifiedName~Erp.Gates.Tests.G2|FullyQualifiedName~Erp.Gates.Tests.Rules' P6 ;;
    L7DB) run L7DB 'FullyQualifiedName~Erp.Gates.Tests.G1.G1DatabaseIsolationTests|FullyQualifiedName~Erp.Gates.Tests.G1.G1TenantSettingCodeTests' L7 ;;
    L7) run L7 'FullyQualifiedName~Erp.Gates.Tests.G1' L7 ;;
    L6) run L6 'FullyQualifiedName~Erp.Gates.Tests.G1' L6 ;;
    NONEG1) run NONEG1 'FullyQualifiedName~Erp.Gates.Tests.G1' NONE ;;
  esac
done
