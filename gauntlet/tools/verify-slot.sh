#!/usr/bin/env bash
# Runs a command (normally `./erp verify`) while holding one of a few machine-wide slots, so parallel
# agents do not run more full verifies at once than the machine can carry. Waits for a free slot.
#   ERP_VERIFY_SLOTS  number of slots (default 2; the owner's PC ran out of memory with three)
#   ERP_SLOT_DIR      folder of the slot lock files (default ~/.verify-slots)
# The slot is held by an open file descriptor and freed when the command exits, however it exits.
set -u
slots="${ERP_VERIFY_SLOTS:-2}"
dir="${ERP_SLOT_DIR:-$HOME/.verify-slots}"
mkdir -p "$dir"
waited=0
while :; do
  for i in $(seq 1 "$slots"); do
    exec 9>>"$dir/slot$i"
    if flock -n 9; then
      echo "verify-slot: slot $i of $slots taken at $(date -u +%FT%TZ) after waiting ${waited} s; time the run from here" >&2
      "$@"
      exit $?
    fi
    exec 9>&-
  done
  sleep 15
  waited=$((waited + 15))
done
