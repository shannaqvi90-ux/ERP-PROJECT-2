import sys, json
sys.argv=[sys.argv[0]]
exec(open('c-api-attack.py').read().split('A = Client')[0])
A = Client("admin@alnoor.example")
items = json.loads(A.req("GET","/api/numbering/sequences?take=500")[1])["items"]
s = [i for i in items if i["companyCode"]=="ALN-DXB" and i["nameEn"]=="Purchase order"][0]
d = json.loads(A.req("GET", f"/api/numbering/sequences/{s['id']}")[1])
log("PO DXB next", d["nextNumber"], "counters", [(c["period"], c["nextValue"], c["lastIssued"]) for c in d["counters"]])
st, body, _ = A.req("PUT", f"/api/numbering/sequences/{s['id']}", {"prefix": d["prefix"], "suffix": d["suffix"], "padding": d["padding"], "reset": d["reset"], "gapless": d["gapless"], "perBranch": False, "nextValue": 500, "version": d["version"]})
log("PUT nextValue 500:", st)
d2 = json.loads(A.req("GET", f"/api/numbering/sequences/{s['id']}")[1])
log("after next", d2["nextNumber"])
st, body, _ = A.req("GET", f"/api/audit/records/numbering.sequences/{s['id']}")
log("audit records numbering.sequences:", st, body[:1500])
