#!/usr/bin/env bash
# Runs inside the toolbox container for ./erp verify. /src is the repository (read-only),
# /out collects results. Stages: suite (web + .NET builds and every unit, integration and gate
# test) and e2e (Playwright against the running stack, then the comparison harness's built ours
# drivers as a health check).
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

step "Copying sources (without build output)"
tar -C /src -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=dist \
    --exclude=test-results --exclude=playwright-report --exclude=.git . | tar -C /work -xf -

case "$stage" in
  suite)
    step "Web: install, type-check, string gate, unit tests, build"
    cd /work/web
    npm ci --no-audit --no-fund --loglevel=error
    npm run --silent typecheck
    npm run --silent check
    npx vitest run --reporter=default --reporter=json --outputFile="$out/vitest.json"
    # G1 in the browser: the client isolation gate must catch every planted client-side leak.
    node scripts/plant-self-test.mjs
    npm run --silent build

    step "Comparison harness (gauntlet/compare): install and unit tests"
    cd /work/gauntlet/compare
    npm ci --no-audit --no-fund --loglevel=error
    # live-odoo.test.mjs needs the Odoo reference rig; critics run it with `npm run test:live`.
    node --test --test-concurrency=1 \
        --test-reporter=spec --test-reporter-destination=stdout \
        --test-reporter=junit --test-reporter-destination="$out/compare-junit.xml" \
        $(ls test/*.test.mjs | grep -v live-odoo)

    step ".NET: restore and build (warnings are errors)"
    cd /work
    dotnet restore Erp.slnx --verbosity quiet
    dotnet build Erp.slnx -c Release --no-restore --verbosity quiet

    step ".NET: unit, integration (Testcontainers PostgreSQL) and gate tests"
    rm -rf "$out/trx"
    dotnet test Erp.slnx -c Release --no-build --verbosity quiet --filter "Load!=Timing" \
        --logger "trx" --logger "console;verbosity=normal" --results-directory "$out/trx"

    # Timing budgets at demo volume (tests/Erp.Testing/TimingBudget.cs) are measured alone, after
    # everything else, so the suite's own parallel tests do not share the machine with them.
    step ".NET: timing budgets at demo volume (alone)"
    dotnet test Erp.slnx -c Release --no-build --verbosity quiet --filter "Load=Timing" \
        --logger "trx" --logger "console;verbosity=normal" --results-directory "$out/trx"
    ;;
  e2e)
    step "End-to-end: Playwright against ${ERP_BASE_URL}"
    cd /work/tests/e2e
    npm ci --no-audit --no-fund --loglevel=error
    ERP_E2E_REPORT="$out/e2e.json" npx playwright test

    # Every built ours driver of the comparison harness still does its task on the product as
    # built (round 3: a list change broke a driver and nothing noticed). A health check: the
    # drivers' set-up creates the dataset records the clean stack lacks; counts are not compared.
    step "Comparison harness: built ours drivers against ${ERP_BASE_URL} (health check)"
    cd /work/gauntlet/compare
    npm ci --no-audit --no-fund --loglevel=error
    COMPARE_OURS_URL="$ERP_BASE_URL" node run.mjs --task built --product ours --health --out "$out/ours-health"
    ;;
  *)
    echo "unknown stage $stage" >&2
    exit 2
    ;;
esac
