import sys, json
sys.argv=[sys.argv[0]]
exec(open('c-api-attack.py').read().split('A = Client')[0])
A = Client("admin@alnoor.example")
items = json.loads(A.req("GET","/api/numbering/sequences?take=500")[1])["items"]
s = [i for i in items if i["companyCode"]=="ALN-DXB" and i["nameEn"]=="Purchase order"][0]
st, body, _ = A.req("GET", f"/api/audit/records/{s['id']}")
j = json.loads(body)
log("status", st, "keys", list(j)[:10])
for e in (j.get("items") or j.get("events") or [])[:4]:
    log(json.dumps({k: e.get(k) for k in ("action","occurredAt","actorName","changes","recordType")}, ensure_ascii=False)[:500])
