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

# CPU seconds this stage's container used (cgroup v1 cpuacct, or v2 cpu.stat), written when the
# stage ends, pass or fail. The ratchet's maximum verify.cpuSeconds is judged on their sum, which
# other agents' load changes far less than the wall time. Not counted: the PostgreSQL containers
# the tests start, the verify stack's own containers and the image build (containers of their own).
cpu_seconds() {
  if [[ -r /sys/fs/cgroup/cpuacct/cpuacct.usage ]]; then
    awk '{ printf "%.1f\n", $1 / 1e9 }' /sys/fs/cgroup/cpuacct/cpuacct.usage
  elif [[ -r /sys/fs/cgroup/cpu.stat ]]; then
    awk '$1 == "usage_usec" { printf "%.1f\n", $2 / 1e6 }' /sys/fs/cgroup/cpu.stat
  fi
}
trap 'cpu_seconds >"$out/cpu-$stage" 2>/dev/null || true' EXIT

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
    # G2 on screen: the same for the tenancy screens (companies, branches, access, workspace).
    node scripts/tenancy-plant-self-test.mjs
    # G2 on screen, by keyboard: the record form kernel's and the screens' key sweeps must catch every
    # planted key or keyboard-reached control that writes, or offers an action, without its permission.
    node scripts/forms-plant-self-test.mjs
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
    # Runtime settings for the test processes only (not the timing stage, which measures the
    # product as it runs in production): idle thread-pool workers block instead of spinning for a
    # processor (the test processes wait mostly on PostgreSQL, and spinning took processor time
    # from the processes doing work), and a 64 MB first GC generation instead of one sized by the
    # processor cache (fewer collections). Measured on the company attack: 240 -> 220 CPU seconds.
    export DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0 DOTNET_GCgen0size=0x4000000
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
    # The end-to-end suite's sign-ins of the last minute still count against the product's sign-in
    # limit, and the harness's budget cannot see them (gauntlet/compare/lib/sign-in-limit.mjs). A
    # sign-in inside a measured part cannot wait out a 429 (on a quiet machine the sign-in task's
    # 'returning' variant was refused and timed out), so the window is waited out first.
    echo "waiting 61 s for the end-to-end suite's sign-ins to leave the sign-in limit's window"
    sleep 61
    cd /work/gauntlet/compare
    npm_install
    COMPARE_OURS_URL="$ERP_BASE_URL" node run.mjs --task built --product ours --health --out "$out/ours-health"
    ;;
  *)
    echo "unknown stage $stage" >&2
    exit 2
    ;;
esac
