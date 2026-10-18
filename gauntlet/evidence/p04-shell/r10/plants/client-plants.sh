#!/usr/bin/env bash
# Critic p04 r10: client plants. Each is applied alone in the plant clone, the whole web unit suite runs, then it is reverted.
set -u
cd /home/shan/critic/p04-shell-r10-plant/web
out=/home/shan/evidence-staging/p04-shell/r10/plants
run() { # id
  git -C .. diff > "$out/plant-client-$1.diff"
  npx vitest run > "$out/plant-client-$1.log" 2>&1; echo "$1 vitest exit $?" | tee -a "$out/client-plants-summary.txt"
  grep -E "Tests .*(passed|failed)" "$out/plant-client-$1.log" | tail -1 | tee -a "$out/client-plants-summary.txt"
  git -C .. checkout -- web/src
}
# P4 (r9 miss): the palette offers an action whose further permissions ('requires') the user lacks.
python3 - <<'PY'
p='src/modules/shell/paletteActions.ts'; s=open(p).read()
s=s.replace("    if (action.requires?.some((permission) => !can(permission))) return false;\n","",1); open(p,'w').write(s)
PY
run P4-requires-ignored
# P6: the shell renders a screen whose permission the user lacks (route permission ignored).
python3 - <<'PY'
p='src/modules/shell/AppShell.tsx'; s=open(p).read()
s=s.replace("const allowedRoute = route && (!route.permission || can(route.permission));","const allowedRoute = route;",1); open(p,'w').write(s)
PY
run P6-route-permission-ignored
# P7: the palette asks every module's record source, whatever the user's permissions.
python3 - <<'PY'
p='src/modules/shell/CommandPalette.tsx'; s=open(p).read()
s=s.replace("const sources = useMemo(() => allowed(extensions.palette, can), [can]);","const sources = useMemo(() => extensions.palette, []);",1); open(p,'w').write(s)
PY
run P7-palette-sources-unfiltered
# C4: the person's recent screens mirrored into the device key erp.navOpen, which sign-out keeps.
python3 - <<'PY'
p='src/modules/shell/AppShell.tsx'; s=open(p).read()
s=s.replace("    if (stored !== null) return stored !== \"0\";","    if (stored !== null) return stored[0] !== \"0\";",1)
s=s.replace("          localStorage.setItem(recentKey(session.user.id), JSON.stringify(next));","          localStorage.setItem(recentKey(session.user.id), JSON.stringify(next));\n          localStorage.setItem(navKey, (localStorage.getItem(navKey) ?? \"1\").slice(0, 1) + JSON.stringify(next));",1)
open(p,'w').write(s)
PY
run C4-recent-in-device-key
