#!/usr/bin/env bash
# Critic p03 r10: each plant alone (S1, S4, L2), gates and identity test projects only. Run through verify-slot.
set -u
SRC=/home/shan/critic/p03-identity-r10-plants
EV=/home/shan/evidence-staging/p03-identity/r10/plants
for plant in S1 S4 L2; do
  OUT=$EV/run-$plant; mkdir -p "$OUT"
  git -C "$SRC" checkout -q -- . && python3 "$EV/apply-r10-plants.py" "$SRC" "$plant" && git -C "$SRC" diff > "$OUT/plant.diff"
  docker run --rm --network host --name c-p03-identity-r10-plants-$plant \
    -v /var/run/docker.sock:/var/run/docker.sock -v "$SRC":/src:ro -v "$OUT":/out \
    -v erp-cache-nuget:/root/.nuget/packages \
    -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -e ERP_TEST_DB_DIRECT=1 -e ERP_TEST_PASSWORD_ITERATIONS=10000 \
    erp-toolbox:pw1.63.0-net10 bash -c '
      mkdir -p /work && tar -C /src -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=dist --exclude=.git . | tar -C /work -xf - && cd /work &&
      dotnet restore Erp.slnx --verbosity quiet && dotnet build Erp.slnx -c Release --no-restore --verbosity quiet &&
      export DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0 DOTNET_GCgen0size=0x4000000 DOTNET_TieredPGO=0 &&
      for proj in tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj tests/Erp.Modules.Identity.Tests/Erp.Modules.Identity.Tests.csproj; do
        dotnet test $proj -c Release --no-build --verbosity quiet --filter "Load!=Timing&Process!=self-http&Process!=self-company&Process!=self-noninterference" \
          --logger "console;verbosity=normal" > /out/$(basename $proj .csproj).log 2>&1; echo "$proj exit $?";
      done'
  echo "plant $plant done"
done
git -C "$SRC" checkout -q -- .
