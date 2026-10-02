#!/usr/bin/env bash
# G3 (owner's bar): a clean clone builds, migrates, seeds and passes all tests with one command,
# and one command takes a clean clone to a running, seeded demo.
# Clones the committed HEAD into a temporary directory (so nothing uncommitted can help), runs
# `./erp verify` there, then `./erp up`, signs in to the demo over HTTP and shuts everything down.
# Uses its own compose project and ports (base + 30) so it never touches a running demo.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
base_http="${ERP_HTTP_PORT:-8080}"
base_db="${ERP_DB_PORT:-5440}"
export ERP_PROJECT="${ERP_PROJECT:-erp}-g3"
export ERP_HTTP_PORT=$((base_http + 30))
export ERP_DB_PORT=$((base_db + 30))
export ERP_VERIFY_HTTP_PORT=$((base_http + 40))
export ERP_VERIFY_DB_PORT=$((base_db + 40))
export ERP_SEED_VOLUME="${ERP_G3_VOLUME:-2000}"

work="$(mktemp -d "${TMPDIR:-/tmp}/erp-g3.XXXXXX")"
cleanup() {
  (cd "$work/erp" 2>/dev/null && ./erp down --volumes >/dev/null 2>&1) || true
  docker image rm "erp-app:${ERP_PROJECT}" "erp-app:${ERP_PROJECT}-verify" >/dev/null 2>&1 || true
  rm -rf "$work"
}
trap cleanup EXIT

echo "G3: cloning $(git -C "$ROOT" rev-parse --short HEAD) into $work/erp"
git clone --quiet --no-hardlinks "$ROOT" "$work/erp"
cd "$work/erp"
git checkout --quiet "$(git -C "$ROOT" rev-parse HEAD)"
test -z "$(git status --porcelain)" || { echo "G3: the clone is not clean" >&2; exit 1; }

echo "G3: ./erp verify in the clean clone"
./erp verify

echo "G3: ./erp up in the clean clone"
./erp up

echo "G3: signing in to the demo"
response="$(./erp http "$ERP_HTTP_PORT" POST /api/auth/sign-in '{"email":"admin@alnoor.example","password":"Demo-Pass-2026"}')"
echo "$response" | head -1
echo "$response" | grep -q '"authenticated":true' || { echo "G3: demo sign-in failed" >&2; exit 1; }
echo "G3 passed: clean clone verified and demo running with seeded data."
