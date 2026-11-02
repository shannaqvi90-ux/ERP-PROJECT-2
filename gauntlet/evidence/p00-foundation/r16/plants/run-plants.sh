#!/usr/bin/env bash
# Critic p00 r16. Runs inside the toolbox: build the planted copy once, then gate suites per plant.
# Plants (plants.diff) are inert unless CRITIC_PLANT names them.
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
  rm -f /tmp/erp-l16-answers.xml
  CRITIC_PLANT="$env" dotnet test "$project" -c Release --no-build --verbosity quiet \
    --filter "($filter)&$base" --logger "console;verbosity=normal" > "/out/plant-$name.log" 2>&1
  echo "=== $name exit $? end $(date -u +%T)"
  grep -E "^\s*(Failed|Total|Skipped) |Failed!|Passed!|Total tests" "/out/plant-$name.log" | tail -40
}
for spec in ${PLANTS}; do
  case "$spec" in
    PROOFNONE) run PROOFNONE 'FullyQualifiedName~CriticR16Proof' NONE ;;
    PROOFALL) run PROOFALL 'FullyQualifiedName~CriticR16Proof' P16,L16 ;;
    G2P16) run G2P16 'FullyQualifiedName~Erp.Gates.Tests.G2|FullyQualifiedName~Erp.Gates.Tests.Rules|FullyQualifiedName~Erp.Gates.Tests.SelfTests' P16 ;;
    G1L16) run G1L16 'FullyQualifiedName~Erp.Gates.Tests.G1' L16 ;;
    G1STATIC) run G1STATIC 'FullyQualifiedName~G1OutsideProcessStateTests|FullyQualifiedName~G1TenantSourceTests|FullyQualifiedName~G1ProcessStateTests|FullyQualifiedName~G1TenantSettingCodeTests|FullyQualifiedName~G2ClientAddressCodeTests' NONE ;;
    MODP16) for m in Kernel Modules.Identity; do run "${m}P16" 'FullyQualifiedName~Erp' P16 tests/Erp.$m.Tests/Erp.$m.Tests.csproj; done ;;
  esac
done
