#!/usr/bin/env bash
# Critic p00 r10. Runs inside the toolbox: build the planted copy once, then gate suites per plant.
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
  grep -E "^\s*(Failed|Passed|Total|Skipped) |Failed!|Passed!|Total tests" "/out/plant-$name.log" | grep -v "^\s*Passed " | tail -30
}
for spec in ${PLANTS:-P5PROOF P5NONE P5 P4 L5}; do
  case "$spec" in
    P5PROOF) run P5PROOF 'FullyQualifiedName~CriticR10Proof' P5 ;;
    P5NONE) run P5NONE 'FullyQualifiedName~CriticR10Proof' NONE ;;
    P5) run P5 'FullyQualifiedName~Erp.Gates.Tests.G2|FullyQualifiedName~Erp.Gates.Tests.Rules' P5 ;;
    P5G1) run P5G1 'FullyQualifiedName~Erp.Gates.Tests.G1' P5 ;;
    P4) run P4 'FullyQualifiedName~Erp.Gates.Tests.G2|FullyQualifiedName~Erp.Gates.Tests.G1.G1ProcessStateTests' P4 ;;
    P4G1) run P4G1 'FullyQualifiedName~Erp.Gates.Tests.G1' P4 ;;
    L5) run L5 'FullyQualifiedName~Erp.Gates.Tests.G1' L5 ;;
  esac
done
