# Critic p03 r7: tenant A's identity exports and reports must hold nothing of tenant B.
import sys, urllib.request; sys.path.insert(0,'.')
from lib import *
a = C("admin@alnoor.example"); b = C("admin@gulfsteel.example")
st, bu = b.get("/api/identity/users?take=200"); st, br = b.get("/api/identity/roles?take=50"); st, bs = b.get("/api/auth/session")
markers = ["gulfsteel", bs["user"]["id"]] + [r["id"] for r in br["items"]] + [u["id"] for u in bu["items"]]
def raw(cl, path):
    rq = urllib.request.Request(B + path, headers={"X-Erp-Request": "1"})
    try:
        with cl.op.open(rq, timeout=300) as r: return r.status, r.headers.get("content-type"), r.read()
    except urllib.error.HTTPError as e: return e.code, e.headers.get("content-type"), e.read()
for path in ["/api/reports/lists/identity.users?format=csv&search=gulfsteel", "/api/reports/lists/identity.users?format=csv&take=500",
             "/api/reports/lists/identity.roles?format=csv", "/api/reports/lists/identity.roles?format=xlsx", "/api/reports/lists/identity.roles?format=pdf&language=ar",
             "/api/reports/run/identity.usersByRole?format=csv", "/api/reports/run/identity.roleSummary?format=csv",
             f"/api/reports/run/identity.usersByRole?format=csv&roleId={br['items'][0]['id']}", "/api/reports/lists/tenancy.access?format=csv"]:
    st, ct, body = raw(a, path)
    text = body.decode("utf-8", "replace")
    hits = [m for m in markers if m in text]
    print(f"{path}: [{st}] {ct} {len(body)} bytes; tenant B markers: {hits[:3]}; head: {text[:120]!r}")
