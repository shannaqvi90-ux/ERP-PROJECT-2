#!/usr/bin/env bash
# Critic p00 r12. Runs inside the toolbox: build the planted copy once, then gate suites per plant.
# Plants (plants.diff) are inert unless CRITIC_PLANT names them.
set -uo pipefail
mkdir -p /work && cd /src && tar -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=.git --exclude=dist . | tar -C /work -xf -
cd /work
echo "build start $(date -u +%T)"
dotnet restore Erp.slnx --verbosity quiet && dotnet build Erp.slnx -c Release --no-restore --verbosity quiet || { echo BUILD FAILED; exit 1; }
echo "build end $(date -u +%T)"
base='Load!=Timing'
run() {
  local name="$1" filter="$2" env="$3"
  echo "=== $name (CRITIC_PLANT=$env) filter $filter start $(date -u +%T)"
  CRITIC_PLANT="$env" dotnet test tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj -c Release --no-build --verbosity quiet \
    --filter "($filter)&$base" --logger "console;verbosity=normal" > "/out/plant-$name.log" 2>&1
  echo "=== $name exit $? end $(date -u +%T)"
  grep -E "^\s*(Failed|Total|Skipped) |Failed!|Passed!|Total tests" "/out/plant-$name.log" | tail -30
}
for spec in ${PLANTS}; do
  case "$spec" in
    PROOFNONE) run PROOFNONE 'FullyQualifiedName~CriticR12Proof' NONE ;;
    PROOFP6) run PROOFP6 'FullyQualifiedName~CriticR12Proof' P6 ;;
    PROOFP7) run PROOFP7 'FullyQualifiedName~CriticR12Proof' P7 ;;
    PROOFP8) run PROOFP8 'FullyQualifiedName~CriticR12Proof' P8 ;;
    PROOFP9) run PROOFP9 'FullyQualifiedName~CriticR12Proof' P9 ;;
    G2P6) run G2P6 'FullyQualifiedName~Erp.Gates.Tests.G2|FullyQualifiedName~Erp.Gates.Tests.Rules' P6 ;;
    G2P7) run G2P7 'FullyQualifiedName~Erp.Gates.Tests.G2|FullyQualifiedName~Erp.Gates.Tests.Rules' P7 ;;
    G2P8) run G2P8 'FullyQualifiedName~Erp.Gates.Tests.G2|FullyQualifiedName~Erp.Gates.Tests.Rules' P8 ;;
    G2P9) run G2P9 'FullyQualifiedName~Erp.Gates.Tests.G2|FullyQualifiedName~Erp.Gates.Tests.Rules' P9 ;;
    G2NONE) run G2NONE 'FullyQualifiedName~Erp.Gates.Tests.G2|FullyQualifiedName~Erp.Gates.Tests.Rules' NONE ;;
    G1L8DB) run G1L8DB 'FullyQualifiedName~Erp.Gates.Tests.G1.G1DatabaseIsolationTests' L8 ;;
    G1L8) run G1L8 'FullyQualifiedName~Erp.Gates.Tests.G1' L8 ;;
    G1L9) run G1L9 'FullyQualifiedName~Erp.Gates.Tests.G1' L9 ;;
    G1P8) run G1P8 'FullyQualifiedName~Erp.Gates.Tests.G1' P8 ;;
    PROOFP10) run PROOFP10 'FullyQualifiedName~CriticR12Proof' P10 ;;
    PROOFNONE2) run PROOFNONE2 'FullyQualifiedName~CriticR12Proof' NONE ;;
    G2P10) run G2P10 'FullyQualifiedName~Erp.Gates.Tests.G2|FullyQualifiedName~Erp.Gates.Tests.Rules' P10 ;;
    G1P10) run G1P10 'FullyQualifiedName~Erp.Gates.Tests.G1.G1ProcessStateTests' P10 ;;
  esac
done
