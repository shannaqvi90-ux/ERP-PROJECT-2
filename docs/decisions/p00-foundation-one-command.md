# p00 — The one command: `./erp`, Docker Compose and a test toolbox image

Date: 2026-10-02. Piece: p00-foundation. Status: accepted.

## Decision

- `./erp` is a bash script needing only Docker (with Compose v2), bash and git:
  - `./erp up` builds the app image, starts PostgreSQL, runs the one-shot `setup` container
    (bootstrap roles and database as superuser → migrations as `erp_owner` → idempotent seed as
    `erp_app`), then serves the app and prints the URL and demo sign-ins.
  - `./erp verify` runs every test: web type-check, string gate, vitest, `dotnet build` (warnings
    are errors), every .NET test (unit, Testcontainers integration, gates), brings up a fresh
    stack exactly like `up` and runs Playwright against it, runs the timing budgets alone, then
    checks `gauntlet/ratchet.json` (counts reached, nothing failed or skipped, no minimum lowered
    since the committed version, wall time on a quiet machine and processor time on any machine
    under their maximums). Independent
    stages run side by side (round 5, `p00-foundation-verify-speed.md`).
  - `./erp verify --clean-clone` (G3) clones HEAD to a temp dir and runs `verify` and `up` there.
- Builds and tests run inside a toolbox image (`build/toolbox.Dockerfile`: the Playwright image
  plus the .NET SDK), so the host needs no SDK, Node or browser. Testcontainers reaches the host
  Docker through the mounted socket.
- Every port and the compose project name come from the environment (`ERP_PROJECT`,
  `ERP_HTTP_PORT`, `ERP_DB_PORT`, `ERP_VERIFY_*`), so several copies run side by side.
- The app image is multi-stage: Node builds the web app, the .NET SDK publishes the host, the
  ASP.NET runtime image serves both as a non-root user.
- The HTTP health probe in the script uses bash `/dev/tcp`, so not even curl is required.

## Why

- G3 demands one command from a clean clone with nothing but Docker, bash and git.
- Running the same `setup` path for the demo and for verify means the demo is what the tests saw.

## Networks that inspect TLS

Some networks (corporate proxies, this build machine) re-terminate TLS with their own CA. Containers
do not inherit the host's trust store, so `npm ci` and `dotnet restore` inside the toolbox and the
image build would fail. `./erp` therefore passes `ERP_EXTRA_CA_CERTS` (defaulting to the host's
`NODE_EXTRA_CA_CERTS` or `SSL_CERT_FILE` when one names a file) into the toolbox as a read-only
mount and into the image build as a BuildKit secret (never a layer of the final image). On an
ordinary machine the variable is unset, the secret is an empty file and nothing changes.

## Docker Desktop (round 6, the owner's PC)

The toolbox runs on the Docker host's network and reaches Testcontainers' published ports at
`TESTCONTAINERS_HOST_OVERRIDE`. On Docker's own Linux engine that is 127.0.0.1. On Docker Desktop
(the owner's PC: Windows, WSL 2, Docker Desktop 29.5) a container on the host network reaches a
port published at a fixed number through 127.0.0.1 but not one published at random, which is how
Testcontainers publishes its resource reaper: the reaper never answered, and every database test
failed after 65 s with `ResourceReaperException: Initialization has been cancelled` (measured with
the reaper image and `nc` from a host-network container: 127.0.0.1 refused, host.docker.internal
answered). `./erp` therefore uses `host.docker.internal` when `docker info` names Docker Desktop
and 127.0.0.1 otherwise; `ERP_TESTCONTAINERS_HOST` overrides it for any other set-up. The test
databases are still reached at their own addresses (`ERP_TEST_DB_DIRECT`), which work on both.
