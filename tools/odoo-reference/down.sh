#!/usr/bin/env bash
# Stop the Odoo reference instance.   down.sh [--purge]   (--purge also deletes its database and filestore volumes)
# Honours ODOO_REF_PROJECT like up.sh; it only ever touches that one compose project.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export ODOO_REF_PROJECT="${ODOO_REF_PROJECT:-b-p01-odoo-rig}"
if [[ "${1:-}" == "--purge" ]]; then
  docker compose -f "$HERE/compose.yaml" down --volumes --remove-orphans
else
  docker compose -f "$HERE/compose.yaml" down --remove-orphans
fi
