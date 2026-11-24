#!/usr/bin/env bash
# Critic p09 r4: build the gate tests of the plant copy (L6 + P6) and run the classes that reach p09's surface.
cd /home/shan/critic/p09-custom-fields-r4-plant
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
dotnet build tests/Erp.Gates.Tests -c Release -v q -nodeReuse:false -p:UseSharedCompilation=false 2>&1 | tail -3
dotnet test tests/Erp.Gates.Tests -c Release --no-build --logger "console;verbosity=normal" \
  --filter "FullyQualifiedName~G1DatabaseIsolationTests|FullyQualifiedName~G2PermissionTests|FullyQualifiedName~CustomFieldGateTests" 2>&1
dotnet build-server shutdown >/dev/null 2>&1
