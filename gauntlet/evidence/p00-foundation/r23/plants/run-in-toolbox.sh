#!/usr/bin/env bash
# Critic p00 r23: starts run-plants.sh inside the toolbox, mounted the way ./erp run_toolbox mounts it (Docker Engine in WSL: 127.0.0.1).
S=/home/shan/evidence-staging/p00-foundation/r23/plants
docker run --rm --name c-p00-foundation-r23-plants --network host -v /var/run/docker.sock:/var/run/docker.sock \
  -v /home/shan/critic/p00-foundation-r23-plant:/src:ro -v $S:/out -v erp-cache-nuget:/root/.nuget/packages \
  -e TESTCONTAINERS_HOST_OVERRIDE=127.0.0.1 -e ERP_TEST_DB_DIRECT=1 -e ERP_TEST_PASSWORD_ITERATIONS=10000 -e CRITIC_STEPS="${CRITIC_STEPS:-}" \
  erp-toolbox:pw1.63.0-net10 bash /out/run-plants.sh
