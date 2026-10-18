#!/usr/bin/env bash
# Critic p04 r11: apply each client plant alone to a clean copy at 682a8ccd and run the whole web unit suite.
set -u
P=/home/shan/evidence-staging/p04-shell/r11/plants
cd /home/shan/critic/p04-shell-r11-C
: > $P/client-plants-summary.txt
for d in $P/plant-client-*.diff; do
  n=$(basename $d .diff)
  git checkout -q -- . && git apply $d || { echo "$n APPLY FAILED" >> $P/client-plants-summary.txt; continue; }
  (cd web && npx vitest run > $P/$n.log 2>&1); rc=$?
  echo "$n exit=$rc $(grep -E 'Tests +[0-9]' $P/$n.log | tail -1)" >> $P/client-plants-summary.txt
  git checkout -q -- .
done
(cd web && npx vitest run > $P/client-baseline.log 2>&1); echo "baseline exit=$? $(grep -E 'Tests +[0-9]' $P/client-baseline.log | tail -1)" >> $P/client-plants-summary.txt
cat $P/client-plants-summary.txt
