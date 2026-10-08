#!/usr/bin/env bash
# Live proof on the demo (critic p03 r5): a helpdesk clerk (identity.users.read + users.update, workspace-wide,
# works in no company) vs "Dubai Manager" (no workspace roles; role "Company manager" in company ALN-DXB only).
B=http://localhost:20350; H=(-H 'X-Erp-Request: 1' -H 'Content-Type: application/json'); DM=01a11ab2-005c-7801-a48e-e427647b8026
j() { echo; echo "\$ $*"; }
j "admin re-activates the manager (PUT)"; v=$(curl -s -b a.jar "${H[@]}" $B/api/identity/users/$DM | python3 -c 'import json,sys;print(json.load(sys.stdin)["version"])')
curl -s -w ' [%{http_code}]' -b a.jar "${H[@]}" -X PUT -d "{\"displayName\":\"Dubai Manager\",\"language\":\"en\",\"isActive\":true,\"version\":$v}" $B/api/identity/users/$DM | head -c 200
j "manager signs in"; curl -s -c m.jar -w ' [%{http_code}]' "${H[@]}" -d '{"email":"dubai.manager@alnoor.example","password":"Manager-Pass-2026x"}' $B/api/auth/sign-in | head -c 120
j "clerk's own permissions"; curl -s -b c.jar "${H[@]}" $B/api/auth/session | python3 -c 'import json,sys;d=json.load(sys.stdin);print(d["user"]["email"],d["permissions"])'
j "manager's access (as admin)"; curl -s -b a.jar "${H[@]}" $B/api/identity/users/$DM/access | head -c 600
v=$(curl -s -b a.jar "${H[@]}" $B/api/identity/users/$DM | python3 -c 'import json,sys;print(json.load(sys.stdin)["version"])')
j "CONTROL clerk PUT /users/{manager} isActive=false"; curl -s -w ' [%{http_code}]' -b c.jar "${H[@]}" -X PUT -d "{\"displayName\":\"Dubai Manager\",\"language\":\"en\",\"isActive\":false,\"version\":$v}" $B/api/identity/users/$DM
j "CONTROL clerk POST /users/{manager}/sessions/revoke"; curl -s -w ' [%{http_code}]' -b c.jar "${H[@]}" -X POST $B/api/identity/users/$DM/sessions/revoke
j "ATTACK clerk POST /users/matching/active {active:false, search: the manager's e-mail, expectedCount:1}"; curl -s -w ' [%{http_code}]' -b c.jar "${H[@]}" -d '{"active":false,"search":"dubai.manager@alnoor.example","filter":"","expectedCount":1}' $B/api/identity/users/matching/active
j "admin reads the manager"; curl -s -b a.jar "${H[@]}" $B/api/identity/users/$DM | head -c 260
j "manager's live session"; curl -s -w ' [%{http_code}]' -b m.jar "${H[@]}" "$B/api/tenancy/companies?take=1" | head -c 200
j "manager signs in again"; curl -s -w ' [%{http_code}]' "${H[@]}" -d '{"email":"dubai.manager@alnoor.example","password":"Manager-Pass-2026x"}' $B/api/auth/sign-in | head -c 200
echo
