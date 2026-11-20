# Test listing for ./erp verify's partition check (sourced by build/verify-inside.sh; tested by
# build/list-tests.test.mjs). Round 21: `dotnet test Erp.slnx --list-tests` runs the projects side by
# side, so their lines interleave (two names on one line, a cut line) and the check failed at random.
# Now one project is listed at a time (one test process, so one writer) and the parser is strict: a
# listing that is not one header and then one well-formed test name per line is an error that says so
# (exit 3, "LISTING BROKEN"), never a missing or an extra test.

# list_parse <label>: reads one project's `dotnet test --list-tests` output on stdin, writes its test
# names (indent removed) in input order. Exit 0 when parsed (a project without tests prints nothing;
# a filter that matches nothing prints the header only), 3 when the listing is broken.
list_parse() {
  local label="$1" line header=0 nomatch=0 seen=0 bad=0
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%$'\r'}"
    [[ -z "${line//[[:space:]]/}" ]] || seen=1
    if [[ "$line" == "The following Tests are available:" ]]; then
      if ((header)); then echo "LISTING BROKEN ($label): a second header" >&2; return 3; fi
      header=1
      continue
    fi
    if [[ "$line" == *"No test matches the given testcase filter"* ]]; then nomatch=1; fi
    if [[ "$line" == "    "* ]]; then
      if ((!header)); then echo "LISTING BROKEN ($label): a test line before the header: $line" >&2; return 3; fi
      local name="${line#    }"
      # Namespace.Class.Method, then optionally the theory arguments in parentheses. A space before
      # the first '(' or outside them means two names ran together (or a log line got in).
      if [[ ! "$name" =~ ^[A-Za-z_][A-Za-z0-9_.+\`]*(\(.*\))?$ ]] || [[ "$name" == *"    "* && "${name%%(*}" == *" "* ]]; then
        echo "LISTING BROKEN ($label): not one test name on a line: '$name'" >&2
        bad=1
        continue
      fi
      printf '%s\n' "$name"
    fi
  done
  if ((bad)); then return 3; fi
  if ((!header)); then
    if ((nomatch || !seen)); then return 0; fi
    echo "LISTING BROKEN ($label): no 'The following Tests are available:' line" >&2
    return 3
  fi
  return 0
}

# list_project <csproj> <dotnet test arguments...>: one project's tests, sorted. dotnet ends with 1
# when no test of the project matches the filter; that is read by list_parse, not by the exit code.
list_project() {
  local project="$1" raw
  shift
  raw="$(dotnet test "$project" "$@" -c Release --no-build --verbosity quiet --list-tests 2>&1 || true)"
  local out rc
  out="$(list_parse "$project" <<<"$raw")" || { rc=$?; printf '%s\n' "$raw" | head -20 >&2; return "$rc"; }
  [[ -z "$out" ]] || printf '%s\n' "$out" | sort
}

# list_tests <csproj or Erp.slnx> <filter arguments...>: for the solution, every test project of it,
# one after the other.
list_tests() {
  local target="$1" p
  shift
  if [[ "$target" == *.slnx ]]; then
    for p in $(grep -o 'tests/[^"]*\.csproj' "$target"); do list_project "$p" "$@" || return $?; done
  else
    list_project "$target" "$@"
  fi
}
