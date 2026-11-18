import sys, json
sys.argv=[sys.argv[0]]
exec(open('c-api-attack.py').read().split('A = Client')[0])
A = Client("admin@alnoor.example")
items = json.loads(A.req("GET","/api/numbering/sequences?take=500")[1])["items"]
s = [i for i in items if i["companyCode"]=="ALN-DXB" and i["nameEn"]=="Tax invoice"][0]
d = json.loads(A.req("GET", f"/api/numbering/sequences/{s['id']}")[1])
log("tax invoice:", d["prefix"], d["padding"], d["reset"], "next", d["nextNumber"], "tax", d["tax"])
base = {"prefix": d["prefix"], "suffix": d["suffix"], "padding": d["padding"], "reset": d["reset"], "gapless": True, "perBranch": False, "version": d["version"]}
def put(label, **kw):
    b = dict(base); b.update(kw)
    st, body, _ = A.req("PUT", f"/api/numbering/sequences/{s['id']}", b)
    code = ""
    try: code = json.dumps(json.loads(body).get("errors") or json.loads(body).get("code"), ensure_ascii=False)[:200]
    except Exception: pass
    log(label, st, code)
put("gapless false", gapless=False)
put("next number lowered to 5", nextValue=5)
put("next number raised to 2000 after numbers given", nextValue=2000)
put("prefix INV- digits 9 (could INV-000001202 repeat? no)", prefix="INV-", padding=9)
put("prefix 'INV-2026-' literal, never (no repeat)", prefix="INV-2026-", reset="never", version=d["version"])
d2 = json.loads(A.req("GET", f"/api/numbering/sequences/{s['id']}")[1])
log("after:", d2["prefix"], d2["reset"], "next", d2["nextNumber"])
# put back
st,_,_ = A.req("PUT", f"/api/numbering/sequences/{s['id']}", {"prefix": d["prefix"], "suffix": "", "padding": d["padding"], "reset": "yearly", "gapless": True, "perBranch": False, "version": d2["version"]})
d3 = json.loads(A.req("GET", f"/api/numbering/sequences/{s['id']}")[1])
log("back to yearly INV-{YYYY}-:", st, "next", d3["nextNumber"])
