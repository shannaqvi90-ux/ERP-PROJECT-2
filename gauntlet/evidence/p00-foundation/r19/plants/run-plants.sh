#!/usr/bin/env bash
# Critic p00 r19. Runs inside the toolbox: build the planted copy once, then gate suites per plant.
# Plants (plants.diff, CriticPlant.cs) are inert unless CRITIC_PLANT names them; CriticR19Proof.cs proves them live.
set -uo pipefail
mkdir -p /work && cd /src && tar -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=.git --exclude=dist . | tar -C /work -xf -
cd /work
echo "build start $(date -u +%T)"
dotnet restore Erp.slnx --verbosity quiet && dotnet build Erp.slnx -c Release --no-restore --verbosity quiet -m:4 || { echo BUILD FAILED; exit 1; }
echo "build end $(date -u +%T)"
base='Load!=Timing'
run() {
  local name="$1" filter="$2" env="$3" project="${4:-tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj}"
  echo "=== $name (CRITIC_PLANT=$env) filter $filter start $(date -u +%T)"
  CRITIC_PLANT="$env" dotnet test "$project" -c Release --no-build --verbosity quiet \
    --filter "($filter)&$base" --logger "console;verbosity=normal" > "/out/plant-$name.log" 2>&1
  echo "=== $name exit $? end $(date -u +%T)"
  grep -E "^\s*(Failed|Total|Skipped) |Failed!|Passed!|Total tests" "/out/plant-$name.log" | tail -40
}
for spec in ${PLANTS}; do
  case "$spec" in
    PROOFNONE) run PROOFNONE 'FullyQualifiedName~CriticR19Proof' NONE ;;
    PROOFL19) run PROOFL19 'FullyQualifiedName~CriticR19Proof' L19 ;;
    PROOFALL) run PROOFALL 'FullyQualifiedName~CriticR19Proof' P17,P19,L19 ;;
    G2P19) run G2P19 'FullyQualifiedName~Erp.Gates.Tests.G2|FullyQualifiedName~Erp.Gates.Tests.Rules|FullyQualifiedName~Erp.Gates.Tests.SelfTests' P19 ;;
    G2P17) run G2P17 'FullyQualifiedName~Erp.Gates.Tests.G2|FullyQualifiedName~Erp.Gates.Tests.Rules|FullyQualifiedName~Erp.Gates.Tests.SelfTests' P17 ;;
    VOLL19) run VOLL19 'FullyQualifiedName~G1ListVolumeTests|FullyQualifiedName~G1NonInterferenceTests|FullyQualifiedName~G1ProcessStateTests|FullyQualifiedName~G1OutsideProcessStateTests' L19 ;;
    MODP17) for m in Kernel Modules.Identity; do run "${m}P17" 'FullyQualifiedName~Erp' P17 tests/Erp.$m.Tests/Erp.$m.Tests.csproj; done ;;
  esac
done
