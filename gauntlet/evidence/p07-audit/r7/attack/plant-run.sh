#!/usr/bin/env bash
# r7 critic: build the planted clone, then the audit module tests and the gate classes that reach p07's surface.
cd /home/shan/critic/p07-audit-r7-plant || exit 1
FILTER="${PLANT_GATE_FILTER:-FullyQualifiedName~G1ProcessStateTests|FullyQualifiedName~G1DatabaseIsolationTests|FullyQualifiedName~G2PermissionTests}"
echo "plant run start $(date -u +%FT%TZ) load $(cut -d' ' -f1-3 /proc/loadavg)"
dotnet build Erp.slnx -c Release -v quiet -nologo 2>&1 | tail -3
if [ -z "${PLANT_SKIP_MODULE:-}" ]; then
echo "== module tests $(date -u +%FT%TZ)"
dotnet test tests/Erp.Modules.Audit.Tests -c Release --no-build --logger "console;verbosity=normal" 2>&1 | grep -E "^\s+(Passed|Failed) |\[FAIL\]|Total tests|Passed:|Failed:|Total time|Assert" | cut -c1-400
fi
echo "== gate classes $(date -u +%FT%TZ) filter $FILTER"
dotnet test tests/Erp.Gates.Tests -c Release --no-build --logger "console;verbosity=normal" --filter "$FILTER" 2>&1 \
  | grep -E "^\s+(Passed|Failed) |\[FAIL\]|Total tests|Passed:|Failed:|Total time|^\s{6,}[a-zA-Z/'].{0,300}" | cut -c1-500
echo "plant run end $(date -u +%FT%TZ) load $(cut -d' ' -f1-3 /proc/loadavg)"
dotnet build-server shutdown >/dev/null 2>&1
