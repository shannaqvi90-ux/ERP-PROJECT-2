#!/usr/bin/env bash
# Critic p00 r14. Runs inside the toolbox: build the planted copy once, then gate suites per plant.
# Plants (plants.diff) are inert unless CRITIC_PLANT names them.
set -uo pipefail
mkdir -p /work && cd /src && tar -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=.git --exclude=dist . | tar -C /work -xf -
cd /work
echo "build start $(date -u +%T)"
dotnet restore Erp.slnx --verbosity quiet && dotnet build Erp.slnx -c Release --no-restore --verbosity quiet || { echo BUILD FAILED; exit 1; }
echo "build end $(date -u +%T)"
base='Load!=Timing'
run() {
  local name="$1" filter="$2" env="$3" project="${4:-tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj}"
  echo "=== $name (CRITIC_PLANT=$env) filter $filter start $(date -u +%T)"
  rm -f /tmp/erp-l11-answers.xml
  CRITIC_PLANT="$env" dotnet test "$project" -c Release --no-build --verbosity quiet \
    --filter "($filter)&$base" --logger "console;verbosity=normal" > "/out/plant-$name.log" 2>&1
  echo "=== $name exit $? end $(date -u +%T)"
  grep -E "^\s*(Failed|Total|Skipped) |Failed!|Passed!|Total tests" "/out/plant-$name.log" | tail -40
}
for spec in ${PLANTS}; do
  case "$spec" in
    PROOFNONE) run PROOFNONE 'FullyQualifiedName~CriticR14Proof' NONE ;;
    PROOFALL) run PROOFALL 'FullyQualifiedName~CriticR14Proof' P13,P14,L11 ;;
    G2P13) run G2P13 'FullyQualifiedName~Erp.Gates.Tests.G2|FullyQualifiedName~Erp.Gates.Tests.Rules|FullyQualifiedName~Erp.Gates.Tests.SelfTests' P13 ;;
    G2P14) run G2P14 'FullyQualifiedName~Erp.Gates.Tests.G2.G2PermissionTests' P14 ;;
    G1L11) run G1L11 'FullyQualifiedName~Erp.Gates.Tests.G1' L11 ;;
    G1STATIC) run G1STATIC 'FullyQualifiedName~G1OutsideProcessStateTests|FullyQualifiedName~G1TenantSourceTests|FullyQualifiedName~G1ProcessStateTests|FullyQualifiedName~G1TenantSettingCodeTests|FullyQualifiedName~G2ClientAddressCodeTests' NONE ;;
    MODP13) for m in Kernel Modules.Lists Modules.Reports Modules.Tenancy; do run "${m}P13" 'FullyQualifiedName~Erp' P13 tests/Erp.$m.Tests/Erp.$m.Tests.csproj; done ;;
    G1P13) run G1P13 'FullyQualifiedName~Erp.Gates.Tests.G1' P13 ;;
    IDP13) run IDP13 'FullyQualifiedName~Erp' P13 tests/Erp.Modules.Identity.Tests/Erp.Modules.Identity.Tests.csproj ;;
  esac
done
