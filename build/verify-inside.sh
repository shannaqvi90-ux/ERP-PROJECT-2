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
    # G2 on screen: the same for the contacts screen (New contact, New person, the form, Archive and Restore).
    node scripts/contacts-plant-self-test.mjs
    # G2 on screen, by keyboard: the record form kernel's and the screens' key sweeps must catch every
    # planted key or keyboard-reached control that writes, or offers an action, without its permission.
    node scripts/forms-plant-self-test.mjs
    # G2 in the command palette: the palette's gate must catch every planted action offered to a
    # user who may not run it (module actions and the screen's own commands).
    node scripts/palette-plant-self-test.mjs
    npm run --silent build

    step "Verify machinery (build/): stopping a verify ends every stage process; the quiet judgement"
    cd /work
    node --test --test-concurrency=1 \
        --test-reporter=spec --test-reporter-destination=stdout \
        --test-reporter=junit --test-reporter-destination="$out/build-junit.xml" \
        build/*.test.mjs

    step "Comparison harness (gauntlet/compare): install and unit tests"
    cd /work/gauntlet/compare
    npm_install
    # live-odoo.test.mjs needs the Odoo reference rig; critics run it with `npm run test:live`.
    node --test --test-concurrency=1 \
        --test-reporter=spec --test-reporter-destination=stdout \
        --test-reporter=junit --test-reporter-destination="$out/compare-junit.xml" \
        $(ls test/*.test.mjs | grep -v live-odoo)
    # Instrument integrity: every planted fault in the measuring instrument (scripts/mutations.mjs,
    # one defence removed or weakened at a time, in a scratch copy) must fail a self-test.
    step "Comparison harness: instrument mutations (each must be caught)"
    node scripts/mutations.mjs
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
    # Tiered PGO off as well: its instrumented tier costs more processor time than it gives back in
    # test processes that each run a few minutes of mostly database-bound work (measured round 8 on
    # G1CompanyIsolationTests and G2AccountTakeoverTests, interleaved runs: 245 and 250 s of user
    # time against 253 and 257 s with it on, about 3 % less).
    export DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=0 DOTNET_GCgen0size=0x4000000 DOTNET_TieredPGO=0
    # Password hashes made by the test hosts (seeded users, users and passwords set through the
    # API) cost 10,000 PBKDF2 rounds instead of the production 210,000 (round 9, measured: the four
    # gate processes made 6,700 derivations, the largest single use of their processor time). The
    # test library hands it to its Testing hosts only (ErpAppFactory), and the product honours it
    # only in the Testing environment (Erp.Kernel.Security.PasswordHashing); hosts tests build in
    # other environments, the timing budgets, the demo and every deployment hash with the production
    # cost, and the decoy of an unknown address keeps the production cost.
    export ERP_TEST_PASSWORD_ITERATIONS=10000
    rm -rf "$out/trx"
    # Test groups side by side, each in a test process of its own, so each has a PostgreSQL server
    # (Testcontainers) and databases of its own (round 20, owner decision #50: the alone verify's
    # wall time was one test, the G1 HTTP attack, 2 h 40 min of a 2 h 53 min stage). Every test still
    # runs exactly once: the groups' filters partition the tests (checked below, before anything runs).
    #   projects          every test project but the gates
    #   gates-rest        the gate tests that are in none of the groups below
    #   noninterference   G1NonInterferenceTests
    #   listvolume        G1ListVolumeTests
    #   http-0 .. http-3  the G1 HTTP attack, one share of the endpoints each (AttackShard.GateCount);
    #                     each leaves its result in $ERP_GATE_SHARD_DIR
    #   http-judge        the verdict on the shares' results, after the four shares are done
    #   self-http, self-company, self-noninterference
    #                     the three long self-tests of the planted module, each in a process of its
    #                     own: their planted state is static, so in one process they would have to
    #                     take turns (they do, in a plain dotnet test); separate processes keep it apart
    gates=tests/Erp.Gates.Tests/Erp.Gates.Tests.csproj
    http_shards=4
    selftests=(self-http self-company self-noninterference)
    in_groups="FullyQualifiedName!~G1HttpIsolation&FullyQualifiedName!~G1NonInterferenceTests&FullyQualifiedName!~G1ListVolumeTests"
    for p in "${selftests[@]}"; do in_groups="$in_groups&Process!=$p"; done
    export ERP_GATE_SHARD_DIR="$out/g1-http-shards"
    rm -rf "$ERP_GATE_SHARD_DIR"
    mkdir -p "$ERP_GATE_SHARD_DIR"
    # Each group's own processor seconds (user + system of the test processes the group's `dotnet test`
    # waited for) go to $out/cpu-group-<name> (round 21: the stage's counted time varied by 4,000 s
    # between alone runs and nothing said which group varied; a report, judged by nothing).
    # (`times` is read in the shell itself: in a command substitution it would see a subshell's children.)
    group_cpu() {
      local f="$out/.times.$BASHPID"
      times >"$f"
      cpu_now="$(tail -1 "$f" | awk '{ split($1, a, /[ms]/); split($2, b, /[ms]/); printf "%.1f", a[1] * 60 + a[2] + b[1] * 60 + b[2] }')"
      rm -f "$f"
    }
    dotnet_test() {
      local name="$1" before status=0
      shift
      group_cpu
      before="$cpu_now"
      dotnet test "$@" -c Release --no-build --verbosity quiet \
          --logger "trx;LogFilePrefix=$name" --logger "console;verbosity=normal" --results-directory "$out/trx" \
          >"$out/dotnet-$name.log" 2>&1 || status=$?
      group_cpu
      awk -v a="$before" -v b="$cpu_now" 'BEGIN { printf "%.1f\n", b - a }' >"$out/cpu-group-$name"
      return "$status"
    }
    # list_tests: one project at a time, strict parser (build/list-tests.sh, round 21)
    source /work/build/list-tests.sh
    lt() { list_tests "$@" || { echo "The test listing is broken (see above); that is not a missing or an extra test."; exit 1; }; }
    step ".NET: the test groups are every test exactly once"
    lt Erp.slnx --filter "Load!=Timing" >"$out/tests-all.txt"
    {
      lt Erp.slnx --filter "Load!=Timing&FullyQualifiedName!~Erp.Gates.Tests"
      lt "$gates" --filter "Load!=Timing&$in_groups"
      lt "$gates" --filter "Load!=Timing&FullyQualifiedName~G1NonInterferenceTests"
      lt "$gates" --filter "Load!=Timing&FullyQualifiedName~G1ListVolumeTests"
      for ((k = 0; k < http_shards; k++)); do lt "$gates" --filter "Shard=$k"; done
      lt "$gates" --filter "Judge=http-attack"
      for p in "${selftests[@]}"; do lt "$gates" --filter "Process=$p"; done
    } >"$out/tests-groups.raw"
    sort "$out/tests-groups.raw" >"$out/tests-groups.txt"
    if [[ ! -s "$out/tests-all.txt" ]] || ! diff -q "$out/tests-all.txt" "$out/tests-groups.txt" >/dev/null; then
      echo "The test groups are not every test exactly once:"
      diff "$out/tests-all.txt" "$out/tests-groups.txt" | head -40
      exit 1
    fi
    echo "$(wc -l <"$out/tests-all.txt") tests, each in exactly one group"
    step ".NET: unit, integration (Testcontainers PostgreSQL) and gate tests"
    rm -rf "$out/trx"
    names=()
    pids=()
    # At most ERP_VERIFY_TEST_GROUPS groups run at once (round 20, owner decision #51): twelve at
    # once on 16 threads oversubscribe the cores and the counted processor time rose 41 %. A group
    # that is waiting for a place starts when another ends; the longest start first. Unset or not
    # a positive number takes the default (documented in ./erp). A group's result goes to a file
    # of its own, so the wait below needs no pid bookkeeping.
    cap="${ERP_VERIFY_TEST_GROUPS:-3}"
    if ! [[ "$cap" =~ ^[0-9]+$ ]] || ((cap < 1)); then cap=3; fi
    echo "test groups at once: $cap"
    rm -f "$out"/dotnet-*.status
    start() {
      local name="$1"
      shift
      while (($(jobs -rp | wc -l) >= cap)); do wait -n || true; done
      names+=("$name")
      (
        if dotnet_test "$name" "$@"; then echo passed >"$out/dotnet-$name.status"; else echo FAILED >"$out/dotnet-$name.status"; fi
      ) &
      pids+=($!)
    }
    # The longest first.
    for ((k = 0; k < http_shards; k++)); do start "http-$k" "$gates" --filter "Shard=$k"; done
    start noninterference "$gates" --filter "Load!=Timing&FullyQualifiedName~G1NonInterferenceTests"
    for p in "${selftests[@]}"; do start "$p" "$gates" --filter "Process=$p"; done
    start listvolume "$gates" --filter "Load!=Timing&FullyQualifiedName~G1ListVolumeTests"
    start gates-rest "$gates" --filter "Load!=Timing&$in_groups"
    start projects Erp.slnx --filter "Load!=Timing&FullyQualifiedName!~Erp.Gates.Tests"
    failed=0
    for i in "${!pids[@]}"; do
      wait "${pids[$i]}" || true
      if [[ "$(cat "$out/dotnet-${names[$i]}.status" 2>/dev/null)" == passed ]]; then
        echo "== ${names[$i]}: passed"
      else
        echo "== ${names[$i]}: FAILED"
        failed=1
      fi
    done
    # The verdict on the HTTP attack's shares runs when every share has left its result (or failed).
    names+=(http-judge)
    if dotnet_test http-judge "$gates" --filter "Judge=http-attack"; then
      echo "== http-judge: passed"
    else
      echo "== http-judge: FAILED"
      failed=1
    fi
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
