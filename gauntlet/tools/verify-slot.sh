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
#   ERP_VERIFY_ALONE  set to 1 for an integration verify (owner's decision, 2026-10-19, needs-human #22):
#                     the run takes every slot, so no other full verify runs beside it and its processor
#                     time is measured on a quiet machine. While it waits, no other run takes a new slot,
#                     so it is not starved; runs already going finish first. Only one alone run waits or
#                     runs at a time (integrators also hold the integration lock).
# Slots are held by open file descriptors and freed when the command exits, however it exits.
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
# The alone file: an alone run holds it exclusively from the moment it starts waiting; other runs only
# look at it (a shared lock taken and dropped at once) and take no new slot while it is held.
exec 7>>"$dir/alone"

if [ "${ERP_VERIFY_ALONE:-}" = "1" ]; then
  until flock -n 7; do sleep 15; waited=$((waited + 15)); done
  echo "verify-slot: alone run waiting for all $slots slots (no other run takes a new slot meanwhile)" >&2
  for i in $(seq 1 "$slots"); do
    fd=$((9 + i))
    eval "exec $fd>>\"\$dir/slot\$i\""
    until flock -n "$fd"; do sleep 15; waited=$((waited + 15)); done
  done
  echo "verify-slot: all $slots slots taken (alone) at $(date -u +%FT%TZ) after waiting ${waited} s; time the run from here" >&2
  "$@"
  exit $?
fi

while :; do
  if flock -n -s 7; then
    flock -u 7
    for i in $(seq 1 "$slots"); do
      exec 9>>"$dir/slot$i"
      if flock -n 9; then
        exec 7>&-
        echo "verify-slot: slot $i of $slots taken at $(date -u +%FT%TZ) after waiting ${waited} s; time the run from here" >&2
        "$@"
        exit $?
      fi
      exec 9>&-
    done
  fi
  sleep 15
  waited=$((waited + 15))
done
