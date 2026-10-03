#!/usr/bin/env bash
# Apply each planted fault to a scratch copy, run the gate suite, record pass/fail counts.
cd /home/user/critic/p01-odoo-rig-r1-plant
P=/home/user/evidence-staging/p01-odoo-rig/r1/plants
for d in a2-replay a4-static-cache a5-rls-off p2-replay p3-no-permission; do
  git checkout -q -- . ; git clean -fdq src tests
  git apply "$P/plant-$d.diff" || { echo "apply failed $d" > "$P/plant-$d.log"; continue; }
  { echo "=== plant $d"; git diff --stat; dotnet test tests/Erp.Gates.Tests -c Release 2>&1; echo "exit=$?"; } > "$P/plant-$d.log" 2>&1
  echo "$d: $(grep -E '^(Failed!|Passed!)' $P/plant-$d.log | tail -1) $(grep -c '^  Failed ' $P/plant-$d.log) failed-tests"
done
git checkout -q -- .
