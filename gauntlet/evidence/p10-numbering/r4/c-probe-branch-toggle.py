import sys, json
sys.argv=[sys.argv[0]]
exec(open('c-api-attack.py').read().split('A = Client')[0])
A = Client("admin@alnoor.example")
items = json.loads(A.req("GET","/api/numbering/sequences?take=500&search=delivery")[1])["items"]
s = [i for i in items if i["companyCode"]=="ALN-DXB" and i["documentType"]=="inventory.delivery"][0]
d = json.loads(A.req("GET", f"/api/numbering/sequences/{s['id']}")[1])
log("before: prefix", d["prefix"], "perBranch", d["perBranch"], "tax", d["tax"], "next", d["nextNumber"])
log("counters:", [(c["branchCode"], c["period"], c["nextValue"], c["lastIssued"]) for c in d["counters"]])
log("recent:", [r["number"] for r in d["recent"]][:5])
st, body, _ = A.req("PUT", f"/api/numbering/sequences/{s['id']}", {"prefix": d["prefix"], "suffix": d["suffix"], "padding": d["padding"], "reset": d["reset"], "gapless": d["gapless"], "perBranch": False, "version": d["version"]})
log("PUT perBranch=false:", st, body[:300])
d2 = json.loads(A.req("GET", f"/api/numbering/sequences/{s['id']}")[1])
log("after: perBranch", d2["perBranch"], "next number previewed:", d2["nextNumber"], "nextValue", d2["nextValue"])
