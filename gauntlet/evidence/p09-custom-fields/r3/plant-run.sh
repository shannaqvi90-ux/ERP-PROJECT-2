#!/usr/bin/env bash
cd /home/shan/critic/p09-custom-fields-r3-plant
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
dotnet build tests/Erp.Gates.Tests -c Release -v q -nodeReuse:false -p:UseSharedCompilation=false 2>&1 | tail -3
dotnet test tests/Erp.Gates.Tests -c Release --no-build --logger "console;verbosity=normal" \
  --filter "FullyQualifiedName~G1TenantSettingCodeTests|FullyQualifiedName~G1TenantValueRuleTests|FullyQualifiedName~G2PermissionTests|FullyQualifiedName~CustomFieldGateTests|FullyQualifiedName~G1ProcessStateTests|FullyQualifiedName~EndpointPermission" 2>&1
