#!/usr/bin/env bash
# Stop the Odoo reference instance.   down.sh [--purge]
# Honours ODOO_REF_PROJECT like up.sh; it only ever touches that one compose project.
#
# The shared rig (project odoo-reference, the default) is used by every critic: stop it only when
# you are its owner. Its data sits in external volumes that `down` keeps; --purge on the shared rig
# also needs ODOO_REF_CONFIRM_PURGE=odoo-reference. A private copy (any other project name) loses
# its volumes with --purge alone.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
say() { printf '[odoo-reference] %s\n' "$*"; }
# shellcheck source=rig.sh
source "$HERE/rig.sh"
if [[ "${1:-}" == "--purge" ]]; then
  if [[ "$RIG_SHARED" == 1 ]]; then
    [[ "${ODOO_REF_CONFIRM_PURGE:-}" == "$SHARED_PROJECT" ]] || {
      echo "refusing to delete the shared reference rig's data; set ODOO_REF_CONFIRM_PURGE=$SHARED_PROJECT to confirm" >&2; exit 1; }
    dc down --remove-orphans
    docker volume rm "${SHARED_VOLUMES[@]}"
  else
    dc down --volumes --remove-orphans
  fi
else
  dc down --remove-orphans
fi
