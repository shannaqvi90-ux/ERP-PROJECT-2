#!/usr/bin/env bash
# Usage: run-plants.sh <plant-name> <target-file-in-repo> <plant-source>
# Applies the planted file in the plant clone, runs the gate suite, restores the original.
set -u
REPO=/home/user/critic/p01-odoo-rig-r2-plants
OUT=/home/user/evidence-staging/p01-odoo-rig/r2/plants
name="$1"; target="$2"; src="$3"
cd "$REPO"
git checkout -q -- .
cp "$src" "$target"
git diff > "$OUT/$name.diff"
s=$(date +%s)
dotnet test tests/Erp.Gates.Tests -c Release --logger "console;verbosity=normal" > "$OUT/$name.log" 2>&1
echo "rc=$? seconds=$(( $(date +%s)-s ))" >> "$OUT/$name.log"
git checkout -q -- .
grep -E "^\s+Failed |Passed!|Failed!|rc=" "$OUT/$name.log" | head -30
