#!/usr/bin/env bash
# Runs inside the toolbox container for ./erp verify. /src is the repository (read-only),
# /out collects results. ./erp runs the stages side by side where they do not depend on each other:
#   web     web app (install, type-check, string gate, unit tests, plant self-tests, build) and the
#           comparison harness's unit tests
#   dotnet  .NET restore and build, then every unit, integration and gate test except the timing
#           budgets; the build stays in /work (a volume of this run) for the timing stage
#   timing  the timing budgets at demo volume, alone, after every other stage (same build)
#   e2e     Playwright against the running stack, then the comparison harness's built ours drivers
set -euo pipefail
stage="${1:?stage}"
out=/out
mkdir -p "$out" /work

step() { printf '\n\033[1m== %s\033[0m\n' "$*"; }

# Networks that re-terminate TLS: ./erp mounts the extra CA certificates here (see ERP_EXTRA_CA_CERTS).
if [[ -s /etc/erp-extra-ca.crt ]]; then
  cat /etc/erp-extra-ca.crt >>/etc/ssl/certs/ca-certificates.crt
  export NODE_EXTRA_CA_CERTS=/etc/erp-extra-ca.crt
fi

copy_sources() {
  step "Copying sources (without build output)"
  tar -C /src -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=dist \
      --exclude=test-results --exclude=playwright-report --exclude=.git . | tar -C /work -xf -
}

npm_install() {
  npm ci --no-audit --no-fund --loglevel=error
}

case "$stage" in
  web)
    copy_sources
    step "Web: install, type-check, string gate, unit tests, build"
    cd /work/web
    npm_install
    npm run --silent typecheck
    npm run --silent check
    npx vitest run --reporter=default --reporter=json --outputFile="$out/vitest.json"
    # G1 in the browser: the client isolation gate must catch every planted client-side leak.
    node scripts/plant-self-test.mjs
    # G2 on screen: the identity screens' permission gate must catch every planted action offered
    # without its permission.
    node scripts/identity-plant-self-test.mjs
    npm run --silent build

    step "Comparison harness (gauntlet/compare): install and unit tests"
    cd /work/gauntlet/compare
    npm_install
    # live-odoo.test.mjs needs the Odoo reference rig; critics run it with `npm run test:live`.
    node --test --test-concurrency=1 \
        --test-reporter=spec --test-reporter-destination=stdout \
        --test-reporter=junit --test-reporter-destination="$out/compare-junit.xml" \
        $(ls test/*.test.mjs | grep -v live-odoo)
    ;;
  dotnet)
    copy_sources
    step ".NET: restore and build (warnings are errors)"
    cd /work
    dotnet restore Erp.slnx --verbosity quiet
    dotnet build Erp.slnx -c Release --no-restore --verbosity quiet

    step ".NET: unit, integration (Testcontainers PostgreSQL) and gate tests"
    rm -rf "$out/trx"
    # Four test processes side by side: every test project (the gate self-tests below excepted),
    # and the three long self-tests of the planted module, each in a process of its own. Their
    # planted state is static, so in one process they would have to take turns (they do, in a
    # plain dotnet test); separate processes keep it apart. Every test still runs exactly once.
    dotnet_test() {
      local name="$1"
      shift
      dotnet test "$@" -c Release --no-build --verbosity quiet \
          --logger "trx;LogFilePrefix=$name" --logger "console;verbosity=normal" --results-directory "$out/trx" \
          >"$out/dotnet-$name.log" 2>&1
    }
    selftests=(self-http self-company self-noninterference)
    exclude="Load!=Timing"
    for p in "${selftests[@]}"; do exclude="$exclude&Process!=$p"; done
    pids=()
    dotnet_test suite Erp.slnx --filter "$exclude" & pids+=($!)
    for p in "${selftests[@]}"; do
      dotnet_test "$p" tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj --filter "Process=$p" & pids+=($!)
    done
    failed=0
    names=(suite "${selftests[@]}")
    for i in "${!pids[@]}"; do
      if wait "${pids[$i]}"; then
        echo "== ${names[$i]}: passed"
      else
        echo "== ${names[$i]}: FAILED"
        failed=1
      fi
    done
    for name in "${names[@]}"; do
      printf '\n\033[1m-- .NET tests: %s\033[0m\n' "$name"
      cat "$out/dotnet-$name.log"
    done
    exit "$failed"
    ;;
  timing)
    # Timing budgets at demo volume (tests/Erp.Testing/TimingBudget.cs) are measured alone, after
    # every other stage, so nothing else shares the machine with them. The build is the dotnet
    # stage's (the same /work volume).
    step ".NET: timing budgets at demo volume (alone)"
    cd /work
    dotnet test Erp.slnx -c Release --no-build --verbosity quiet --filter "Load=Timing" \
        --logger "trx" --logger "console;verbosity=normal" --results-directory "$out/trx"
    ;;
  e2e)
    copy_sources
    step "End-to-end: Playwright against ${ERP_BASE_URL}"
    cd /work/tests/e2e
    npm_install
    ERP_E2E_REPORT="$out/e2e.json" npx playwright test

    # Every built ours driver of the comparison harness still does its task on the product as
    # built (round 3: a list change broke a driver and nothing noticed). A health check: the
    # drivers' set-up creates the dataset records the clean stack lacks; counts are not compared.
    step "Comparison harness: built ours drivers against ${ERP_BASE_URL} (health check)"
    cd /work/gauntlet/compare
    npm_install
    COMPARE_OURS_URL="$ERP_BASE_URL" node run.mjs --task built --product ours --health --out "$out/ours-health"
    ;;
  *)
    echo "unknown stage $stage" >&2
    exit 2
    ;;
esac
