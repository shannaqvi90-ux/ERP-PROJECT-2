# p05 — `./erp verify` on Docker Desktop: test containers reach published ports on host.docker.internal

Date: 2026-10-05. Piece: p05-list-search (round 4; a fix outside the piece, kept minimal). Status: accepted.

## Context

The run moved to the owner's PC: Ubuntu on WSL2 with Docker Desktop. There every database test
inside `./erp verify` failed in its fixture with `ResourceReaperException: Initialization has been
cancelled`. The test toolbox runs with `--network host` and told Testcontainers to reach published
ports on `127.0.0.1` (`TESTCONTAINERS_HOST_OVERRIDE`). Testcontainers' resource reaper (Ryuk) is
reached through a published port Docker picks at random. Measured from a `--network host`
container on this machine:

| Address | Randomly published port |
|---|---|
| `127.0.0.1` | connection refused |
| `172.17.0.1` (bridge gateway) | connection refused |
| `host.docker.internal` / `192.168.65.254` | answers |

The same tests pass outside the toolbox (WSL's own `dotnet test`), where Docker Desktop forwards
the port to WSL's localhost.

## Decision

`run_toolbox` in `./erp` keeps `127.0.0.1` on a Linux Docker engine and passes
`host.docker.internal` when `docker info` reports Docker Desktop. `ERP_TESTCONTAINERS_HOST`
overrides the choice. Database connections themselves are unchanged (`ERP_TEST_DB_DIRECT`: the
tests reach PostgreSQL at the container's own address).

## Why not

- *Disabling the reaper* (`TESTCONTAINERS_RYUK_DISABLED=true`): test containers of a crashed or
  killed test process would stay behind on a machine several agents share.
- *Leaving the toolbox off the host network*: the end-to-end stage and the container-address
  database connections rely on it.
