#!/usr/bin/env bash
# Usage: plant-e2e-run.sh <source copy> <base url> <spec...>: runs end-to-end specs of a planted copy in the toolbox image.
set -u
src="$1"; base="$2"; shift 2
docker run --rm --network host --name c-p05-list-search-r14-plante2e -v "$src":/src:ro -v erp-cache-npm:/root/.npm \
  -e ERP_BASE_URL="$base" -e ERP_DEMO_PASSWORD=Demo-Pass-2026 erp-toolbox:pw1.63.0-net10 bash -c "
  mkdir -p /work && tar -C /src -cf - --exclude=bin --exclude=obj --exclude=node_modules --exclude=dist --exclude=.git . | tar -C /work -xf - &&
  cd /work/tests/e2e && npm ci --no-audit --no-fund --loglevel=error && npx playwright test $* --reporter=list; echo EXIT \$?"
