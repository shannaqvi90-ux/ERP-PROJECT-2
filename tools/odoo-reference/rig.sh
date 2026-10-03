# Shared by up.sh and down.sh (sourced, not run).
#
# The shared rig is the compose project "odoo-reference" (the default). Its data lives in two
# external volumes (compose.shared.yaml) that no `docker compose down -v` can remove. Any other
# ODOO_REF_PROJECT is a private copy with ordinary project-scoped volumes.
#
# Earlier rigs ran under the project "b-p01-odoo-rig", a name builder clean-up rules matched.
# adopt_legacy_rig moves that rig's data into the shared volumes without copying it, then removes
# the old containers, so there is never more than one rig on the port and no data is lost.

SHARED_PROJECT="odoo-reference"
SHARED_VOLUMES=(odoo-reference-db odoo-reference-filestore)
LEGACY_PROJECT="b-p01-odoo-rig"
LEGACY_VOLUMES=(b-p01-odoo-rig_db b-p01-odoo-rig_filestore)

export ODOO_REF_PROJECT="${ODOO_REF_PROJECT:-$SHARED_PROJECT}"
COMPOSE_FILES=(-f "$HERE/compose.yaml")
if [[ "$ODOO_REF_PROJECT" == "$SHARED_PROJECT" ]]; then
  COMPOSE_FILES+=(-f "$HERE/compose.shared.yaml")
  RIG_SHARED=1
else
  RIG_SHARED=0
fi

dc() { docker compose "${COMPOSE_FILES[@]}" "$@"; }

volume_exists() { docker volume inspect "$1" >/dev/null 2>&1; }

# Create the shared rig's external volumes when missing (labelled so people can find them; not
# labelled with a compose project, so no project clean-up selects them).
ensure_shared_volumes() {
  [[ "$RIG_SHARED" == 1 ]] || return 0
  local v
  for v in "${SHARED_VOLUMES[@]}"; do
    volume_exists "$v" || docker volume create --label org.erp.role=odoo-reference "$v" >/dev/null
  done
}

# Move one volume's contents into another. On a host that can reach Docker's volume directories
# (Linux, root) this is a rename inside one file system: instant, no extra disk. Elsewhere a
# helper container copies the data (the source is then left as it was).
move_volume_data() {
  local from="$1" to="$2" src dst
  src="$(docker volume inspect -f '{{.Mountpoint}}' "$from")"
  dst="$(docker volume inspect -f '{{.Mountpoint}}' "$to")"
  if [[ -d "$src" && -d "$dst" && -w "$dst" ]] && [[ "$(stat -c %d "$src")" == "$(stat -c %d "$dst")" ]]; then
    shopt -s dotglob nullglob
    local entries=("$src"/*)
    shopt -u dotglob nullglob
    if ((${#entries[@]})); then mv "${entries[@]}" "$dst"/; fi
    echo moved
  else
    docker run --rm -v "$from:/from:ro" -v "$to:/to" postgres:17-alpine sh -c 'cp -a /from/. /to/' >/dev/null
    echo copied
  fi
}

# One-time adoption of a rig started under the legacy project name.
adopt_legacy_rig() {
  [[ "$RIG_SHARED" == 1 ]] || return 0
  volume_exists "${LEGACY_VOLUMES[0]}" || return 0
  # Already adopted (the shared volume holds a database): nothing to do.
  if volume_exists "${SHARED_VOLUMES[0]}"; then
    local held
    held="$(docker run --rm -v "${SHARED_VOLUMES[0]}:/v:ro" postgres:17-alpine sh -c 'test -f /v/PG_VERSION && echo yes || echo no')"
    [[ "$held" == yes ]] && return 0
  fi
  say "adopting the rig of the legacy project $LEGACY_PROJECT (data moved, not reseeded)"
  ensure_shared_volumes
  local legacy
  legacy="$(docker ps -aq --filter "label=com.docker.compose.project=$LEGACY_PROJECT" --filter label=com.docker.compose.service=odoo)"
  legacy+=" $(docker ps -aq --filter "label=com.docker.compose.project=$LEGACY_PROJECT" --filter label=com.docker.compose.service=db)"
  # Odoo first, then PostgreSQL with a clean shutdown (checkpoint), so the data directory is consistent.
  # shellcheck disable=SC2086
  [[ -n "${legacy// /}" ]] && docker stop -t 120 $legacy >/dev/null
  local i how
  for i in "${!LEGACY_VOLUMES[@]}"; do
    volume_exists "${LEGACY_VOLUMES[$i]}" || continue
    how="$(move_volume_data "${LEGACY_VOLUMES[$i]}" "${SHARED_VOLUMES[$i]}")"
    say "  ${LEGACY_VOLUMES[$i]} -> ${SHARED_VOLUMES[$i]} ($how)"
  done
  # The old containers would hold port 8069 and point at the old volumes: remove them (stopped,
  # their data already moved). The old volumes are removed only when the move emptied them.
  # shellcheck disable=SC2086
  [[ -n "${legacy// /}" ]] && docker rm $legacy >/dev/null
  for i in "${!LEGACY_VOLUMES[@]}"; do
    local left
    left="$(docker run --rm -v "${LEGACY_VOLUMES[$i]}:/v:ro" postgres:17-alpine sh -c 'ls -A /v | wc -l')"
    if [[ "$left" == 0 ]]; then docker volume rm "${LEGACY_VOLUMES[$i]}" >/dev/null && say "  removed the emptied ${LEGACY_VOLUMES[$i]}"; fi
  done
  docker network rm "${LEGACY_PROJECT}_default" >/dev/null 2>&1 || true
}
