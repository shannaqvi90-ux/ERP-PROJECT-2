#!/usr/bin/env bash
# Runs the gate project (tests/Erp.Gates.Tests) on a planted copy, in the product's own toolbox image,
# the way ./erp verify's dotnet stage does (copy sources, restore, build, test), without the planted-module
# self-tests and the timing budgets.  Usage: run-gates.sh <planted copy> <out dir> [extra filter]
set -u
src="$1"; out="$2"; extra="${3:-}"
mkdir -p "$out"
filter="Load!=Timing&Process!=self-http&Process!=self-company&Process!=self-noninterference${extra:+&$extra}"
docker run --rm --name "c-p06-form-report-r3-gates-$(basename "$src")" --network host \
  -v /var/run/docker.sock:/var/run/docker.sock -v "$src":/src:ro -v "$out":/out \
  -v erp-cache-nuget:/root/.nuget/packages -v erp-cache-npm:/root/.npm \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -e ERP_TEST_DB_DIRECT=1 \
  -e DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0 -e DOTNET_GCgen0size=0x4000000 \
  erp-toolbox:pw1.63.0-net10 bash -c "
    mkdir -p /work && tar -C /src -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=dist --exclude=.git . | tar -C /work -xf - &&
    cd /work && dotnet restore Erp.slnx --verbosity quiet && dotnet build Erp.slnx -c Release --no-restore --verbosity quiet &&
    dotnet test tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj -c Release --no-build --verbosity quiet --filter '$filter' \
      --logger 'trx;LogFileName=gates.trx' --logger 'console;verbosity=normal' --results-directory /out/trx; echo EXIT \$?"
