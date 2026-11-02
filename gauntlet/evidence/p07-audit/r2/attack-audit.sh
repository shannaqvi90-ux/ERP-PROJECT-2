#!/usr/bin/env bash
# Critic attack: tenant A (Al Noor admin) against tenant B (Gulf Steel) on the audit surface.
B=http://localhost:20750
J=$(mktemp -d)
signin() { curl -s -c $J/$1 -H 'X-Erp-Request: 1' -H 'content-type: application/json' -d "{\"email\":\"$2\",\"password\":\"Demo-Pass-2026\"}" $B/api/auth/sign-in -o /dev/null -w "sign-in $2 %{http_code}\n"; }
signin a admin@alnoor.example
signin b admin@gulfsteel.example
signin n noaccess@alnoor.example
signin v viewer@alnoor.example
csrf() { grep -i xsrf $J/$1 | awk '{print $7}' | head -1; }
# Tenant B's own audit rows
curl -s -b $J/b "$B/api/audit/log?take=5&filter=recordType%20eq%20'tenancy.companies'" > $J/blog.json
python3 - $J/blog.json <<'PY' > $J/bids
import json,sys
d=json.load(open(sys.argv[1]))
items=d.get('items',d.get('rows',[]))
for r in items[:5]: print(r.get('id'), r.get('recordId'), r.get('recordType'), r.get('recordLabel') or r.get('record'))
PY
echo "tenant B sees:"; cat $J/bids
read BUID BREC BTYPE BLABEL < <(head -1 $J/bids)
echo "--- as tenant A"
for u in "/api/audit/log/$BUID" "/api/audit/records/$BREC" "/api/audit/records/$BREC?type=$BTYPE" "/api/audit/records/$BREC?before=$BUID" \
         "/api/audit/log?filter=recordId%20eq%20'$BREC'" "/api/audit/log?filter=recordType%20eq%20'tenancy.companies'&take=200" "/api/audit/log?recordId=$BREC" "/api/audit/log?search=Gulf" "/api/audit/log?q=Gulf%20Steel" \
         "/api/reports/lists/audit.log?format=csv&search=Gulf" "/api/reports/lists/audit.log?format=csv" ; do
  code=$(curl -s -b $J/a -o $J/out -w '%{http_code}' "$B$u")
  hits=$(grep -o -i "gulf\|0190a000-0000-7000-8000-000000000002\|$BREC\|$BUID" $J/out | sort | uniq -c | tr '\n' ' ')
  echo "$code $(wc -c <$J/out)B $u  B-hits:[$hits]"
done
echo "--- tenant headers as A"
for h in "X-Tenant-Id: 0190a000-0000-7000-8000-000000000002" "X-Tenant: gulfsteel" ; do
  code=$(curl -s -b $J/a -H "$h" -o $J/out -w '%{http_code}' "$B/api/audit/records/$BREC")
  echo "$code $h $(grep -c "$BREC" $J/out)"
done
echo "--- noaccess and viewer (no audit.log.read?)"
for who in n v; do for u in "/api/audit/log" "/api/audit/log/$BUID" "/api/audit/records/$BREC" "/api/reports/lists/audit.log?format=csv"; do
  echo "$who $(curl -s -b $J/$who -o /dev/null -w '%{http_code}' "$B$u") $u"; done; done
echo "--- anonymous"
for u in "/api/audit/log" "/api/audit/records/$BREC" "/api/reports/lists/audit.log?format=csv"; do echo "anon $(curl -s -o /dev/null -w '%{http_code}' "$B$u") $u"; done
rm -rf $J
