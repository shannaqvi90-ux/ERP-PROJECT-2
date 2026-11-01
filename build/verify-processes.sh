# Process handling for ./erp verify (sourced by ./erp; tested by build/verify-processes.test.mjs).
#
# A verify stage is a background subshell running a pipeline (`docker run … | tee … | sed …`), and
# the end-to-end stage a loop that waits for the .NET stage. Killing the subshell alone leaves its
# pipeline running: on 2026-10-09 a SIGTERM to ./erp verify left the e2e loop, tee and sed alive,
# holding the verify slot's file descriptor for about 40 minutes (critic p04 round 7). These
# functions end a stage with everything it started, by process id only: every process they signal
# descends from a process id this run started itself, never one matched by name.

# process_tree <pid>: the process and every process descended from it, parents before children.
process_tree() {
  ps -A -o pid= -o ppid= 2>/dev/null | awk -v root="$1" '
    { children[$2] = children[$2] " " $1; seen[$1] = 1 }
    END {
      if (!(root in seen)) exit
      queue[1] = root; head = 1; tail = 1
      while (head <= tail) {
        pid = queue[head++]; print pid
        n = split(children[pid], kids, " ")
        for (i = 1; i <= n; i++) if (kids[i] != "") queue[++tail] = kids[i]
      }
    }'
}

# stop_process_trees <pid...>: ends each process and all it started. The trees are frozen first
# (SIGSTOP, so no process starts another while they are being ended), collected again (anything
# started in between), then sent SIGTERM and let go (SIGCONT); whatever is still there after
# ERP_STOP_GRACE_SECONDS (default 10) gets SIGKILL.
stop_process_trees() {
  local all=() pid round
  for round in 1 2; do
    all=()
    for pid in "$@"; do
      [[ -n "$pid" ]] || continue
      # shellcheck disable=SC2207
      all+=($(process_tree "$pid"))
    done
    ((${#all[@]} > 0)) || return 0
    kill -STOP "${all[@]}" 2>/dev/null || true
  done
  kill -TERM "${all[@]}" 2>/dev/null || true
  kill -CONT "${all[@]}" 2>/dev/null || true
  local grace="${ERP_STOP_GRACE_SECONDS:-10}" waited=0 alive
  while :; do
    alive=()
    for pid in "${all[@]}"; do
      # A zombie (ended, not yet reaped) counts as ended.
      if kill -0 "$pid" 2>/dev/null && [[ "$(ps -o stat= -p "$pid" 2>/dev/null)" != Z* ]]; then alive+=("$pid"); fi
    done
    ((${#alive[@]} == 0)) && return 0
    ((waited >= grace * 5)) && break
    sleep 0.2
    waited=$((waited + 1))
  done
  kill -KILL "${alive[@]}" 2>/dev/null || true
}

# sample_load <file> <seconds>: appends "<unix time> <one-minute load average> <CPUs>" to the file
# every <seconds> until it is ended (./erp verify ends it with the stages). The ratchet judges the
# wall time only when every sample of the run was quiet (build/ratchet-check.mjs).
sample_load() {
  local file="$1" every="$2" cpus
  cpus="$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 0)"
  [[ -r /proc/loadavg ]] || return 0
  while :; do
    echo "$(date +%s) $(cut -d' ' -f1 /proc/loadavg) $cpus" >>"$file"
    sleep "$every"
  done
}

# machine_busy_ticks [<cpu line>]: the whole machine's busy processor time in clock ticks from the
# aggregate "cpu" line of /proc/stat (or the line given): user + nice + system + irq + softirq +
# steal. Idle and iowait are not busy; guest time is already inside user. WSL2 distributions and
# Docker Desktop's containers share one kernel, so this covers every process on the machine, the
# verify's own and any other work beside it. Prints nothing and returns 1 when the line is not one.
machine_busy_ticks() {
  local line="${1:-}" key user nice system idle iowait irq softirq steal rest
  if [[ -z "$line" ]]; then
    [[ -r /proc/stat ]] || return 1
    IFS= read -r line </proc/stat || return 1
  fi
  read -r key user nice system idle iowait irq softirq steal rest <<<"$line"
  [[ "$key" == cpu ]] || return 1
  local field
  for field in "$user" "$nice" "$system" "$idle" "$iowait" "$irq" "$softirq"; do
    [[ "$field" =~ ^[0-9]+$ ]] || return 1
  done
  [[ "${steal:-0}" =~ ^[0-9]+$ ]] || steal=0
  echo $((user + nice + system + irq + softirq + steal))
}

# machine_cpu_start <out-dir>: records the machine's busy ticks at the start of a verify.
machine_cpu_start() {
  local ticks
  ticks="$(machine_busy_ticks)" || return 0
  echo "$ticks" >"$1/machine-cpu-start"
}

# machine_cpu_end <out-dir>: writes cpu-machine, the processor seconds the whole machine was busy
# between machine_cpu_start and now (two decimals), beside the cpu-<stage> files. Report only: the
# ratchet check prints it and judges nothing by it.
machine_cpu_end() {
  local out="$1" start ticks hz delta
  [[ -r "$out/machine-cpu-start" ]] || return 0
  start="$(<"$out/machine-cpu-start")"
  ticks="$(machine_busy_ticks)" || return 0
  hz="$(getconf CLK_TCK 2>/dev/null || echo 100)"
  ((hz > 0 && ticks >= start)) || return 0
  delta=$((ticks - start))
  printf '%d.%02d\n' $((delta / hz)) $(((delta % hz) * 100 / hz)) >"$out/cpu-machine"
}
