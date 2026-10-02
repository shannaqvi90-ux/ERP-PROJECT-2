#!/usr/bin/env bash
# Runs inside the toolbox container for ./erp verify. /src is the repository (read-only),
# /out collects results. Stages: suite (web + .NET builds and every unit, integration and gate
# test) and e2e (Playwright against the running stack).
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
    npm run --silent build

    step ".NET: restore and build (warnings are errors)"
    cd /work
    dotnet restore Erp.slnx --verbosity quiet
    dotnet build Erp.slnx -c Release --no-restore --verbosity quiet

    step ".NET: unit, integration (Testcontainers PostgreSQL) and gate tests"
    rm -rf "$out/trx"
    dotnet test Erp.slnx -c Release --no-build --verbosity quiet \
        --logger "trx" --logger "console;verbosity=normal" --results-directory "$out/trx"
    ;;
  e2e)
    step "End-to-end: Playwright against ${ERP_BASE_URL}"
    cd /work/tests/e2e
    npm ci --no-audit --no-fund --loglevel=error
    ERP_E2E_REPORT="$out/e2e.json" npx playwright test
    ;;
  *)
    echo "unknown stage $stage" >&2
    exit 2
    ;;
esac
