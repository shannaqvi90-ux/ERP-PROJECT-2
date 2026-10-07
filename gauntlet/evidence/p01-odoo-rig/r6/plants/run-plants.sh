#!/usr/bin/env bash
# Critic r6 plants, from a clone at b9919e6 (gauntlet/compare, after npm ci):
#   cp <this folder>/zz-critic-r6.test.mjs test/ && node --test test/zz-critic-r6.test.mjs   # S1, S2, T3 end 'verified' (MISSED)
#   cp <this folder>/zz-critic-r6-variant.test.mjs test/ && node --test test/zz-critic-r6-variant.test.mjs   # variant setup never called
# Real drivers against ERP_SEED_USERS_CSV=gauntlet/compare/data/out/users.csv ./erp up:
#   git apply plant-S1-edit-and-save-read-continuation.diff && COMPARE_OURS_URL=http://localhost:<port> node run.mjs --task edit-and-save --product ours
#     -> verified, steps 0, keys 0 (honest: 3 / 17); npm test stays green (npm-test-with-edit-and-save-plant.log)
#   git apply plant-S1-find-user-read-continuation.diff && ... --task find-user --product ours
#     -> verified, steps 1, keys 0, human 2.65 s (honest: 4 / 20 / 14.35 s; Odoo 6 / 21 / 14.55 s)
# Instrument mutations: OUT=<dir> ./run-mutations.sh
set -e
cd "${HARNESS:-.}"
node --test test/zz-critic-r6.test.mjs || true
