#!/usr/bin/env bash
# Tenant A (alnoor) administrator attacks tenant B (gulfsteel) through the identity API.
B=http://localhost:20350
J=/tmp/critic-p03-cookies-a.txt; rm -f $J
H=(-H 'Content-Type: application/json' -H 'X-Erp-Request: 1')
curl -s -c $J "${H[@]}" -X POST $B/api/auth/sign-in -d '{"email":"admin@alnoor.example","password":"Demo-Pass-2026"}' -o /dev/null -w "sign-in A admin: %{http_code}\n"
BU=01a10037-3ff7-75a9-a1e7-68b113eae8cd   # B admin
BR=01a10037-3f45-7f07-9a1a-f1b1f575bfbe   # B Administrator role
BRO=01a10037-3f45-7b0d-9473-a8f3bec4c03b  # B Read-only role
BT=0190a000-0000-7000-8000-000000000002
req() { local m=$1 p=$2 d=${3:-}; shift 3 2>/dev/null; local out; if [[ -n "$d" ]]; then out=$(curl -s -b $J "${H[@]}" "$@" -X $m "$B$p" -d "$d" -w " [%{http_code}]"); else out=$(curl -s -b $J "${H[@]}" "$@" -X $m "$B$p" -w " [%{http_code}]"); fi; local leak=""; echo "$out" | grep -qiE "gulfsteel|$BU|$BR|$BRO|$BT" && leak="  <-- B VALUE IN RESPONSE"; echo "$m $p -> ${out: -5}$leak"; }
req GET /api/identity/users/$BU
req GET /api/identity/users/$BU/access
req GET /api/identity/users/$BU/sign-ins
req PUT /api/identity/users/$BU '{"displayName":"pwned","language":"en","isActive":false,"roleIds":[],"version":1}'
req POST /api/identity/users/$BU/password '{}'
req POST /api/identity/users/$BU/unblock ''
req POST /api/identity/users/$BU/sessions/revoke ''
req GET /api/identity/roles/$BR
req PUT /api/identity/roles/$BRO '{"nameEn":"x","nameAr":"x","permissions":[],"version":1}'
req DELETE /api/identity/roles/$BRO
req POST /api/identity/roles/$BR/copy '{"nameEn":"Stolen admin","nameAr":"x"}'
req POST /api/identity/users '{"email":"critic.x1@alnoor.example","displayName":"X","language":"en","roleIds":["'$BRO'"]}'
req GET "/api/identity/users?search=gulfsteel"
req GET "/api/identity/users?search=admin@gulfsteel.example"
req GET "/api/identity/users?search=x&tenantId=$BT"
req GET /api/identity/users/$BU '' -H "X-Tenant-Id: $BT" -H "X-Tenant: gulfsteel" -H "X-Workspace: gulfsteel"
req GET /api/tenancy/tenant '' -H "X-Tenant-Id: $BT"
req PUT /api/me/preferences '{"language":"ar"}' 
# Same e-mail as a B user in tenant A: must be allowed and say nothing about B
req POST /api/identity/users '{"email":"viewer@gulfsteel.example","displayName":"Same address","language":"en","password":"Critic-Pass-2026x","mustChangePassword":false,"roleIds":[]}'
req POST /api/identity/users '{"email":"nobody.critic@gulfsteel.example","displayName":"Control","language":"en","password":"Critic-Pass-2026x","mustChangePassword":false,"roleIds":[]}'
# Sign in as A's credentials into workspace B
curl -s "${H[@]}" -X POST $B/api/auth/sign-in -d '{"email":"admin@alnoor.example","password":"Demo-Pass-2026","workspace":"gulfsteel"}' -w " [%{http_code}]\n" | grep -o '"tenant":{[^}]*}\|\[[0-9]*\]$'
# The same address now in both workspaces, A's password: which workspace does sign-in pick, and is B listed?
curl -s "${H[@]}" -X POST $B/api/auth/sign-in -d '{"email":"viewer@gulfsteel.example","password":"Critic-Pass-2026x"}' -w " [%{http_code}]\n" | grep -o '"tenant":{[^}]*}\|"workspaces":\[[^]]*\]\|\[[0-9]*\]$'
curl -s "${H[@]}" -X POST $B/api/auth/sign-in -d '{"email":"viewer@gulfsteel.example","password":"Demo-Pass-2026"}' -w " [%{http_code}]\n" | grep -o '"tenant":{[^}]*}\|"workspaces":\[[^]]*\]\|\[[0-9]*\]$'
