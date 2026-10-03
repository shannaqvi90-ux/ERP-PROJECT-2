#!/usr/bin/env bash
# Reproduce the critic r3 instrument plants in a clone at b0981bd.
#   1. Generic plants (stand-in page, no product needed): each test PASSES when the plant is MISSED.
#      cp critic-plants.test.mjs gauntlet/compare/test/zz-critic-plants.test.mjs
#      (cd gauntlet/compare && node --test --test-concurrency=1 test/zz-critic-plants.test.mjs)
#   2. Real ours drivers (needs the demo: ERP_SEED_USERS_CSV=gauntlet/compare/data/out/users.csv ./erp up):
#      git apply plant-H2-start-state-find-user.diff   # find-user: 4 steps -> 1, still "verified"
#      git apply plant-K1-chain-sign-in.diff           # sign-in: human 19.06 s -> 16.36 s (new device), 7.63 -> 6.28 (returning)
#      (cd gauntlet/compare && COMPARE_OURS_URL=http://localhost:$ERP_HTTP_PORT node run.mjs --task find-user,sign-in --product ours --out /tmp/x && npm test)
#      npm test stays green (137 tests, 117 pass, 20 live skipped, 0 fail) with either plant applied.
set -euo pipefail
