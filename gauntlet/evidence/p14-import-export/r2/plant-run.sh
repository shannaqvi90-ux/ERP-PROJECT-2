#!/usr/bin/env bash
# Critic p14 r2: runs the gate classes that reach the planted download cache and weaker download permission, in the plant clone.
cd /home/shan/critic/p14-import-export-r2-plant
export TESTCONTAINERS_HOST_OVERRIDE=127.0.0.1
dotnet test tests/Erp.Gates.Tests -c Release --logger "trx;LogFileName=plant.trx" --results-directory /home/shan/critic/p14-import-export-r2-plant/plant-results \
  --filter "FullyQualifiedName~G1ImportExportAttackTests|FullyQualifiedName~G2PermissionTests|FullyQualifiedName~G1ProcessStateTests|FullyQualifiedName~ImportExportWriteSetTests"
rc=$?
dotnet build-server shutdown >/dev/null 2>&1
exit $rc
