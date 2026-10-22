#!/usr/bin/env bash
# Critic p00 r13. Runs inside the toolbox: build the planted copy once, then gate suites per plant.
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
  rm -f /tmp/erp-warm-totals.xml
  CRITIC_PLANT="$env" dotnet test "$project" -c Release --no-build --verbosity quiet \
    --filter "($filter)&$base" --logger "console;verbosity=normal" > "/out/plant-$name.log" 2>&1
  echo "=== $name exit $? end $(date -u +%T)"
  grep -E "^\s*(Failed|Total|Skipped) |Failed!|Passed!|Total tests" "/out/plant-$name.log" | tail -40
}
for spec in ${PLANTS}; do
  case "$spec" in
    PROOFNONE) run PROOFNONE 'FullyQualifiedName~CriticR13Proof' NONE ;;
    PROOFP11) run PROOFP11 'FullyQualifiedName~CriticR13Proof' P11 ;;
    PROOFL10) run PROOFL10 'FullyQualifiedName~CriticR13Proof' L10 ;;
    G2P11) run G2P11 'FullyQualifiedName~Erp.Gates.Tests.G2|FullyQualifiedName~Erp.Gates.Tests.Rules|FullyQualifiedName~Erp.Gates.Tests.SelfTests' P11 ;;
    G2P12) run G2P12 'FullyQualifiedName~Erp.Gates.Tests.G2|FullyQualifiedName~Erp.Gates.Tests.Rules' P12 ;;
    G1L10) run G1L10 'FullyQualifiedName~Erp.Gates.Tests.G1' L10 ;;
    G1P11) run G1P11 'FullyQualifiedName~Erp.Gates.Tests.G1' P11 ;;
    G1STATIC) run G1STATIC 'FullyQualifiedName~G1OutsideProcessStateTests|FullyQualifiedName~G1TenantSourceTests|FullyQualifiedName~G1ProcessStateTests|FullyQualifiedName~G1TenantSettingCodeTests' NONE ;;
    IDP11) run IDP11 'FullyQualifiedName~Erp' P11 tests/Erp.Modules.Identity.Tests/Erp.Modules.Identity.Tests.csproj ;;
    KERNELP11) run KERNELP11 'FullyQualifiedName~Erp' P11 tests/Erp.Kernel.Tests/Erp.Kernel.Tests.csproj ;;
  esac
done
