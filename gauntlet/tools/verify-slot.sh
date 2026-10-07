#!/usr/bin/env bash
# Runs a command (normally `./erp verify`) while holding one of a few machine-wide slots, so parallel
# agents do not run more full verifies at once than the machine can carry. Waits for a free slot.
#   ERP_VERIFY_SLOTS  number of slots (default 2; the owner's PC ran out of memory with three)
#   ERP_SLOT_DIR      folder of the slot lock files (default ~/.verify-slots)
#   ERP_VERIFY_LOG    optional file for the command's output (stdout and stderr, plus this script's
#                     own lines). Agents share one scratch folder, so name it after your role, piece
#                     and round, for example "<scratch>/verify-builder-p02-tenancy-r6.log". The log is
#                     locked before it is emptied, and the script refuses to start (exit 75) while
#                     another run still holds the same log, so one agent cannot wipe another's log by
#                     choosing the same name. Do not also redirect with `> file`: the shell would empty
#                     the file before this script could check the lock.
# The slot is held by an open file descriptor and freed when the command exits, however it exits.
set -u
slots="${ERP_VERIFY_SLOTS:-2}"
dir="${ERP_SLOT_DIR:-$HOME/.verify-slots}"
log="${ERP_VERIFY_LOG:-}"
mkdir -p "$dir"

if [ -n "$log" ]; then
  mkdir -p "$(dirname "$log")"
  # Open without truncating, take the log's own lock, and only then empty it.
  exec 8>>"$log"
  if ! flock -n 8; then
    echo "verify-slot: $log is held by another run; choose a log name of your own (role, piece, round)" >&2
    exit 75
  fi
  : >"$log"
  echo "verify-slot: output goes to $log" >&2
  exec >>"$log" 2>&1
fi

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
