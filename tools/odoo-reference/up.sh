#!/usr/bin/env bash
# Start the Odoo Community reference instance and load it with comparison volume.
# Idempotent: safe to run again at any time; it only adds what is missing.
#
#   tools/odoo-reference/up.sh            start, create the database, install apps, seed, verify counts
#   tools/odoo-reference/up.sh --status   print what is running and the recorded volume
#   tools/odoo-reference/down.sh          stop (add --purge to delete its volumes)
#
# Environment overrides (defaults in brackets):
#   ODOO_REF_PROJECT [odoo-reference]   compose project name; the default is the shared rig every critic
#                                       reuses (external volumes, see compose.shared.yaml). Any other name
#                                       is a private copy whose own `down -v` removes its data.
#   ODOO_REF_PORT    [8069]             host port for the web client
#   ODOO_REF_DB      [reference]        database name
#   ODOO_REF_TARGET  [100000]           minimum rows per main list
#   ODOO_REF_VOLUME_OUT [gauntlet/reference/odoo/volume.json]  where verified counts are written
#
# Sign-ins (local reference instance only, never exposed beyond 127.0.0.1):
#   admin / admin   approver / approver (purchase approver)   buyer / buyer (purchase user)
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
export ODOO_REF_PORT="${ODOO_REF_PORT:-8069}"
export ODOO_REF_DB="${ODOO_REF_DB:-reference}"
DATA_DIR="$ROOT/gauntlet/compare/data/out"
TARGET="${ODOO_REF_TARGET:-100000}"
VOLUME_OUT="${ODOO_REF_VOLUME_OUT:-$ROOT/gauntlet/reference/odoo/volume.json}"
APPS="base,base_setup,contacts,mail,purchase,base_import"
LANGS="ar_001"

say() { printf '[odoo-reference] %s\n' "$*"; }
# shellcheck source=rig.sh
source "$HERE/rig.sh"
psql_ref() { dc exec -T db psql -U odoo -d "$ODOO_REF_DB" -Atc "$1"; }

if [[ "${1:-}" == "--status" ]]; then
  dc ps
  [[ -f "$VOLUME_OUT" ]] && cat "$VOLUME_OUT"
  exit 0
fi

command -v docker >/dev/null || { echo "docker is required" >&2; exit 1; }
command -v node >/dev/null || { echo "node (>= 20) is required to generate the shared dataset" >&2; exit 1; }
docker info >/dev/null 2>&1 || { echo "the Docker daemon is not running" >&2; exit 1; }

started=$(date +%s)
say "generating the shared dataset (deterministic, cached)"
node "$ROOT/gauntlet/compare/data/generate.mjs" >/dev/null

adopt_legacy_rig
ensure_shared_volumes
say "starting PostgreSQL (project $ODOO_REF_PROJECT)"
dc up -d --wait db >/dev/null

if [[ "$(dc exec -T db psql -U odoo -d postgres -Atc "SELECT 1 FROM pg_database WHERE datname = '$ODOO_REF_DB'")" != "1" ]] \
   || [[ "$(psql_ref "SELECT count(*) FROM ir_module_module WHERE state = 'installed' AND name IN ('contacts','purchase','base_import','mail')" 2>/dev/null)" != "4" ]] \
   || [[ "$(psql_ref "SELECT count(*) FROM res_lang WHERE code = 'ar_001' AND active" 2>/dev/null)" != "1" ]]; then
  say "creating database $ODOO_REF_DB and installing $APPS with Arabic (first run takes about a minute)"
  dc run --rm --no-deps odoo odoo -d "$ODOO_REF_DB" -i "$APPS" --load-language="$LANGS" --without-demo=True --stop-after-init >/tmp/odoo-ref-init.$$.log 2>&1 \
    || { tail -40 /tmp/odoo-ref-init.$$.log >&2; exit 1; }
  rm -f /tmp/odoo-ref-init.$$.log
else
  say "database $ODOO_REF_DB already has the apps and Arabic"
fi

say "starting Odoo on http://localhost:$ODOO_REF_PORT"
dc up -d odoo >/dev/null

say "seeding to at least $TARGET rows per main list"
seed_log="$(mktemp)"
# A one-off container mounts the seed script and the dataset; the server container mounts neither,
# so it never depends on the checkout that started it.
if ! dc run --rm --no-deps -T -e RIG_TARGET="$TARGET" -v "$HERE/seed:/seed:ro" -v "$DATA_DIR:/data:ro" \
     odoo odoo shell -d "$ODOO_REF_DB" --no-http --log-level=warn <"$HERE/seed/seed.py" >"$seed_log" 2>&1; then
  tail -40 "$seed_log" >&2; exit 1
fi
grep '^RIG step' "$seed_log" | sed 's/^RIG /  /'
volume_line="$(grep '^RIG VOLUME ' "$seed_log" | tail -1 | sed 's/^RIG VOLUME //')"
rm -f "$seed_log"
[[ -n "$volume_line" ]] || { echo "seed did not report volume" >&2; exit 1; }
# Planner statistics again from outside Odoo (the seed commits its own ANALYZE too): without them
# PostgreSQL plans the bulk tables as empty and some Odoo screens take minutes.
psql_ref "ANALYZE" >/dev/null

say "waiting for the web client"
for _ in $(seq 1 90); do
  code=$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$ODOO_REF_PORT/web/login" || true)
  [[ "$code" == "200" ]] && break
  sleep 2
done
[[ "$code" == "200" ]] || { echo "Odoo did not answer on port $ODOO_REF_PORT" >&2; dc logs --tail 40 odoo >&2; exit 1; }

mkdir -p "$(dirname "$VOLUME_OUT")"
image="$(dc config --images | grep -m1 odoo || true)"
VOLUME_LINE="$volume_line" TARGET="$TARGET" IMAGE="$image" SECONDS_TAKEN="$(( $(date +%s) - started ))" \
node --input-type=module -e '
  import fs from "node:fs";
  const v = JSON.parse(process.env.VOLUME_LINE);
  const target = Number(process.env.TARGET);
  const main = ["contacts", "users", "currency_rates", "audit_messages", "attachments", "job_runs", "approvals"];
  const short = main.filter(k => !(v.lists[k] && v.lists[k].count >= target));
  const out = {
    generated_at: new Date().toISOString(),
    image: process.env.IMAGE, odoo_version: v.odoo_version, minimum_per_main_list: target,
    main_lists: main, ok: short.length === 0, short_lists: short,
    seconds: Number(process.env.SECONDS_TAKEN), lists: v.lists,
  };
  fs.writeFileSync(process.argv[1], JSON.stringify(out, null, 2) + "\n");
  for (const k of Object.keys(v.lists)) console.log(`  ${k.padEnd(18)} ${String(v.lists[k].count).padStart(8)}${main.includes(k) ? (v.lists[k].count >= target ? "  ok" : "  SHORT") : ""}`);
  if (short.length) { console.error(`lists below ${target}: ${short.join(", ")}`); process.exit(1); }
' "$VOLUME_OUT"
say "volume verified and written to ${VOLUME_OUT#$ROOT/}"
say "ready: http://localhost:$ODOO_REF_PORT  (admin/admin, approver/approver, buyer/buyer)"
