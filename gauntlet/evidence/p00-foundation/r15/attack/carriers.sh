#!/usr/bin/env bash
# Critic p00 r10: does the product accept a session token anywhere other than Bearer and the erp_session cookie?
B=${BASE:-http://localhost:20050}
tok=$(curl -s -X POST $B/api/auth/sign-in -H 'X-Erp-Request: 1' -H 'Content-Type: application/json' -d '{"email":"admin@alnoor.example","password":"Demo-Pass-2026","issueToken":true}' | python3 -c 'import sys,json;print(json.load(sys.stdin)["token"])')
echo "token length ${#tok}"
t() { printf '%-60s %s\n' "$1" "$(curl -s -o /dev/null -w '%{http_code}' "${@:2}")"; }
t "bearer (control)" -H "Authorization: Bearer $tok" "$B/api/identity/users?take=1"
t "cookie (control)" -H "Cookie: erp_session=$tok" "$B/api/identity/users?take=1"
t "query access_token" "$B/api/identity/users?take=1&access_token=$tok"
t "query token" "$B/api/identity/users?take=1&token=$tok"
t "query erp_session" "$B/api/identity/users?take=1&erp_session=$tok"
t "header X-Api-Key" -H "X-Api-Key: $tok" "$B/api/identity/users?take=1"
t "header X-Erp-Session" -H "X-Erp-Session: $tok" "$B/api/identity/users?take=1"
t "Authorization: Token" -H "Authorization: Token $tok" "$B/api/identity/users?take=1"
t "Authorization: Basic" -H "Authorization: Basic $(printf 'x:%s' $tok | base64 -w0)" "$B/api/identity/users?take=1"
t "cookie other name" -H "Cookie: session=$tok" "$B/api/identity/users?take=1"
t "bearer lowercase scheme" -H "Authorization: bearer $tok" "$B/api/identity/users?take=1"
t "bearer, path without /api prefix case" -H "Authorization: Bearer $tok" "$B/API/identity/users?take=1"
