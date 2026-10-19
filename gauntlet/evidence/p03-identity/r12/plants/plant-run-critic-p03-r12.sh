#!/usr/bin/env bash
docker run --rm --name c-p03-identity-r12-plant-dotnet --network host -v /var/run/docker.sock:/var/run/docker.sock -v /home/shan/critic/p03-identity-r12-plant:/src:ro -v /tmp/claude-1000/-home-shan-ERP-PROJECT-2/0f5b5ee2-6ce0-400e-ad35-b5cf3fd0b57e/scratchpad/plant-out-critic-p03-r12:/out -v erp-cache-nuget:/root/.nuget/packages -v erp-cache-npm:/root/.npm -v c-p03-identity-r12-plant-work:/work -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -e ERP_TEST_DB_DIRECT=1 erp-toolbox:pw1.63.0-net10 bash /src/build/verify-inside.sh dotnet
echo "planted dotnet stage exit $?"
