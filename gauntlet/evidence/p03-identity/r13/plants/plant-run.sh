#!/usr/bin/env bash
# Runs plant-inside.sh against the planted clone in the toolbox image (through verify-slot by the caller).
OUT=/tmp/claude-1000/-home-shan-ERP-PROJECT-2/0f5b5ee2-6ce0-400e-ad35-b5cf3fd0b57e/scratchpad/plant-out-critic-p03-r13
mkdir -p $OUT
docker run --rm --name c-p03-identity-r13-plant-dotnet --network host -v /var/run/docker.sock:/var/run/docker.sock \
  -v /home/shan/critic/p03-identity-r13-plant:/src:ro -v /home/shan/evidence-staging/p03-identity/r13/plants/plant-inside.sh:/plant-inside.sh:ro \
  -v $OUT:/out -v erp-cache-nuget:/root/.nuget/packages -v c-p03-identity-r13-plant-work:/work \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -e ERP_TEST_DB_DIRECT=1 erp-toolbox:pw1.63.0-net10 bash /plant-inside.sh
echo "planted run exit $?"
