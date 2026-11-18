#!/usr/bin/env bash
cd /home/shan/critic/p09-custom-fields-r3
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
dotnet build tests/Erp.Modules.CustomFields.Tests -c Release -v q -nodeReuse:false -p:UseSharedCompilation=false 2>&1 | tail -3
dotnet test tests/Erp.Modules.CustomFields.Tests -c Release --no-build --logger "console;verbosity=normal" 2>&1
