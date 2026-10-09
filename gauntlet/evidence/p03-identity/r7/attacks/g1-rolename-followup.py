import sys; sys.path.insert(0,'.')
from lib import *
b = C("admin@gulfsteel.example"); a = C("admin@alnoor.example")
st, br = b.get("/api/identity/roles?take=50"); st, ar = a.get("/api/identity/roles?take=200")
an = {r["nameEn"] for r in ar["items"]}
for r in br["items"]:
    print(r["nameEn"], "| also in A:", r["nameEn"] in an)
T = tag(); st, nr = b.post("/api/identity/roles", {"nameEn": f"OnlyB {T}", "nameAr": f"ب {T}", "permissions": []}); print("B creates", st)
show("A creates a role with a name only B holds", a.post("/api/identity/roles", {"nameEn": f"OnlyB {T}", "nameAr": "س"+T, "permissions": []}))
show("A creates a role with an unused name", a.post("/api/identity/roles", {"nameEn": f"Unused {T}", "nameAr": "س"+T, "permissions": []}))
