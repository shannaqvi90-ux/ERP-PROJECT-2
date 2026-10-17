#!/usr/bin/env bash
# Critic p02 r10: apply each web plant alone, run the whole web unit suite, record caught or missed, revert.
cd /home/shan/critic/p02-tenancy-r10-web/web
out=/home/shan/evidence-staging/p02-tenancy/r10/plants/web
for name in "$@"; do
  git checkout -q -- src
  python3 $out/plant.py "$name" || { echo "$name: MISSING TEXT" >> $out/summary.txt; continue; }
  git diff > $out/$name.diff
  npx vitest run > $out/$name.log 2>&1; rc=$?
  failed=$(grep -E "Tests .*failed" $out/$name.log | sed 's/\x1b\[[0-9;]*m//g' | tr -s ' ')
  passed=$(grep -E "^ +Tests " $out/$name.log | sed 's/\x1b\[[0-9;]*m//g' | tr -s ' ')
  if [ $rc -ne 0 ]; then echo "$name: CAUGHT (rc=$rc) $passed" >> $out/summary.txt; else echo "$name: MISSED (rc=0) $passed" >> $out/summary.txt; fi
done
git checkout -q -- src
