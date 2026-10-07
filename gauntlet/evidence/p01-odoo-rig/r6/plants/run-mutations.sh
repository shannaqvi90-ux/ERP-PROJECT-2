#!/usr/bin/env bash
# Critic r6: mutate the instrument (one fault at a time), run the harness's own unit tests that
# cover that part, record whether they catch it, then restore the file with git.
cd "$(dirname "$0")" 2>/dev/null; H=${HARNESS:-/home/shan/critic/p01-odoo-rig-r6-plants/gauntlet/compare}
cd "$H"
mut() { # name file sed-expr tests...
  local name=$1 file=$2 expr=$3; shift 3
  sed -i "$expr" "$file"
  if git diff --quiet -- "$file"; then echo "MUTATION $name: NOT APPLIED"; return; fi
  git diff -- "$file" > "$OUT/mutation-$name.diff"
  if COMPARE_LIVE= timeout 900 node --test --test-concurrency=1 "$@" > "$OUT/mutation-$name.log" 2>&1; then echo "MUTATION $name: MISSED (tests pass: $*)"; else echo "MUTATION $name: caught ($(grep -c '^not ok' "$OUT/mutation-$name.log") failing)"; fi
  git checkout -- "$file"
}
mut M1-klm-K "lib/klm.mjs" "s/K: 0.28, P: 1.1/K: 0.2, P: 1.1/" test/klm.test.mjs test/baselines.test.mjs
mut M2-no-greyscale "lib/blind.mjs" "s/html { filter: grayscale(100%) !important; }/html { }/" test/blind.test.mjs test/compare.test.mjs
mut M3-sentinel-click "lib/guard.mjs" "s/\['click', 'focus', 'blur', 'showPopover'/['focus', 'blur', 'showPopover'/" test/guard.test.mjs
mut M4-lockdown-socket "lib/sandbox/lockdown.mjs" "s/^lock(net.Socket.prototype, 'connect'.*$//" test/sandbox.test.mjs
mut M5-no-freeze "lib/runner.mjs" "s/thaw = await freezePages(context);/thaw = null;/" test/guard.test.mjs test/sandbox.test.mjs
mut M6-identity-words "lib/blind.mjs" "s/identityWords: \['alnoor'\],/identityWords: [],/" test/blind.test.mjs test/compare.test.mjs
