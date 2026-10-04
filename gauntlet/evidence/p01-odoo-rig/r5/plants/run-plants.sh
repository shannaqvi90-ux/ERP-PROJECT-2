#!/usr/bin/env bash
# Critic r5 plants for p01-odoo-rig at 09d6d2c. Run from a clone's gauntlet/compare after `npm ci`.
# EV = this evidence folder (gauntlet/evidence/p01-odoo-rig/r5).
set -u
EV="${EV:-../evidence/p01-odoo-rig/r5}"
# 1. Stand-in plants (no product needed): T2 wrong timer, U4 module-load fetch, U5 child_process, C2/C3 paste.
cp "$EV/plants/zz-critic-r5.test.mjs" test/ && node --test --test-reporter=spec test/zz-critic-r5.test.mjs | grep -E 'RESULT|✔|✖'
rm -f test/zz-critic-r5.test.mjs
# 2. Real-driver plants against our demo (ERP_SEED_USERS_CSV=gauntlet/compare/data/out/users.csv ./erp up):
git apply "$EV/plants/plant-U5-api-update-user-child-process.diff" "$EV/plants/plant-T2-create-restricted-user-wait-in-verify.diff"
node --test --test-concurrency=1 $(ls test/*.test.mjs | grep -v live-odoo) | grep -E '^# (pass|fail)'   # stays green
COMPARE_OURS_URL="${COMPARE_OURS_URL:-http://localhost:8080}" node run.mjs --task api-update-user,create-restricted-user --product ours --out /tmp/critic-r5-plants
git checkout -- drivers/ours/api-update-user.mjs drivers/ours/create-restricted-user.mjs
