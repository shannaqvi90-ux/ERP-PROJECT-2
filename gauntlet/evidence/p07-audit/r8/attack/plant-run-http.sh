#!/usr/bin/env bash
# r8 critic: the G1 HTTP attack on the planted clone, the way ./erp verify runs it: four shares (3 at once), then the judge.
cd /home/shan/critic/p07-audit-r8-plant || exit 1
export ERP_GATE_SHARD_DIR=/home/shan/critic/p07-audit-r8-plant/.g1-http-shards
rm -rf "$ERP_GATE_SHARD_DIR"; mkdir -p "$ERP_GATE_SHARD_DIR"
echo "plant http run start $(date -u +%FT%TZ) load $(cut -d' ' -f1-3 /proc/loadavg)"
run() { dotnet test tests/Erp.Gates.Tests -c Release --no-build --logger "console;verbosity=normal" --filter "$1" > "/home/shan/evidence-staging/p07-audit/r8/attack/plant-http-$2.log" 2>&1; echo "== $2 exit $? $(date -u +%FT%TZ)"; }
run "Shard=0" s0 & run "Shard=1" s1 & run "Shard=2" s2 & wait
run "Shard=3" s3
run "Judge=http-attack" judge
for f in /home/shan/evidence-staging/p07-audit/r8/attack/plant-http-*.log; do echo "-- $f"; grep -E "\[FAIL\]|Total tests|Passed:|Failed:|actors|Leak|leak" "$f" | cut -c1-400 | head -30; done
echo "plant http run end $(date -u +%FT%TZ) load $(cut -d' ' -f1-3 /proc/loadavg)"
dotnet build-server shutdown >/dev/null 2>&1
