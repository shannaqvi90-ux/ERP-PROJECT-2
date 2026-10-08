#!/usr/bin/env bash
# Tenant A (alnoor) attacks tenant B (gulfsteel) over HTTP and as the app's database role.
B=${1:-http://localhost:20450}; P=Demo-Pass-2026; T=$(mktemp -d)
signin() { curl -s -c "$T/$1" -H 'X-Erp-Request: 1' -H 'Content-Type: application/json' -d "{\"email\":\"$2\",\"password\":\"$P\"}" "$B/api/auth/sign-in"; }
A() { curl -s -b "$T/a" -H 'X-Erp-Request: 1' -H 'Content-Type: application/json' "$@"; }
Bc() { curl -s -b "$T/b" -H 'X-Erp-Request: 1' -H 'Content-Type: application/json' "$@"; }
sa=$(signin a admin@alnoor.example); sb=$(signin b admin@gulfsteel.example)
AT=$(jq -r .tenant.id <<<"$sa"); AU=$(jq -r .user.id <<<"$sa"); BT=$(jq -r .tenant.id <<<"$sb"); BU=$(jq -r .user.id <<<"$sb")
echo "A tenant $AT user $AU; B tenant $BT user $BU"
echo "--- B's own prefs before"; Bc "$B/api/auth/session" | jq -c '.user'
echo "--- A PUT /me/preferences with B ids in body, query and headers"
A -X PUT -H "X-Tenant-Id: $BT" -H "X-User-Id: $BU" -H "X-Tenant: gulfsteel" "$B/api/identity/me/preferences?userId=$BU&tenantId=$BT" -d "{\"language\":\"ar\",\"numerals\":\"arab\",\"userId\":\"$BU\",\"tenantId\":\"$BT\",\"id\":\"$BU\"}" -w ' [%{http_code}]\n'
echo "--- B's prefs after (must be unchanged)"; Bc "$B/api/auth/session" | jq -c '.user'
A -X PUT "$B/api/identity/me/preferences" -d '{"language":"en","numerals":"latn"}' -o /dev/null -w 'A reset [%{http_code}]\n'
echo "--- A reads B's user, roles, tenant by id"
A "$B/api/identity/users/$BU" -w ' [%{http_code}]\n' | cut -c1-200
A "$B/api/identity/users/$BU/access" -w ' [%{http_code}]\n' | cut -c1-200
A "$B/api/identity/users?q=gulfsteel" | jq -c '{total, n:(.items|length)}'
A "$B/api/identity/users?filter=email~gulfsteel" -w ' [%{http_code}]\n' | cut -c1-200
A -H "X-Tenant-Id: $BT" "$B/api/tenancy/tenant" | jq -c '{code, id}'
A "$B/api/lists/identity.users/views" -w ' [%{http_code}]\n' | cut -c1-200
echo "--- cache headers"; curl -s -D - -o /dev/null -b "$T/a" "$B/api/auth/session" | grep -i 'cache-control\|set-cookie\|vary'
curl -s -D - -o /dev/null -b "$T/a" "$B/api/identity/users?take=1" | grep -i 'cache-control\|vary'
echo "--- file paths"; for p in /print /../../etc/passwd "/api/files/$BT" "/assets/../../appsettings.json" /appsettings.json /.env; do curl -s -o /dev/null -w "$p [%{http_code}]\n" -b "$T/a" "$B$p"; done
echo "$AT $BT $AU $BU" > "$T/ids"; cp "$T/ids" /tmp/critic-p04-r3-ids 2>/dev/null; rm -rf "$T"
