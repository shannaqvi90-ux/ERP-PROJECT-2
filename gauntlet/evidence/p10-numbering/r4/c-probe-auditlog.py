import sys, json
sys.argv=[sys.argv[0]]
exec(open('c-api-attack.py').read().split('A = Client')[0])
A = Client("admin@alnoor.example")
st, body, _ = A.req("GET", "/api/audit/log?take=5&sort=-occurredAt")
j = json.loads(body)
log("status", st)
for e in j.get("items", [])[:5]:
    log(json.dumps(e, ensure_ascii=False)[:600])
