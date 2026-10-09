# Critic p03 r7: repeated failed sign-ins, then the right password; what the administrator sees.
import sys; sys.path.insert(0,'.')
from lib import *
a = C("admin@alnoor.example"); T = tag()
email = f"lock.{T}@alnoor.example"
st, u = a.post("/api/identity/users", {"email": email, "displayName": f"Lock {T}", "language": "en", "password": "Critic-Pass-2026x", "mustChangePassword": False, "roleIds": []}); print("create", st)
for i in range(12):
    st, r = C().post("/api/auth/sign-in", {"email": email, "password": f"wrong-{i}-Pass"}); print(i + 1, st, (r or {}).get("code") if isinstance(r, dict) else r)
st, r = C().post("/api/auth/sign-in", {"email": email, "password": "Critic-Pass-2026x"}); print("right password after failures:", st, r.get("code") if isinstance(r, dict) else r)
show("sign-in history", a.get(f"/api/identity/users/{u['id']}/sign-ins"))
show("unblock", a.post(f"/api/identity/users/{u['id']}/unblock"))
st, r = C().post("/api/auth/sign-in", {"email": email, "password": "Critic-Pass-2026x"}); print("right password after unblock:", st)
show("reset password", a.post(f"/api/identity/users/{u['id']}/password", {}))
st, r = C().post("/api/auth/sign-in", {"email": email, "password": "Critic-Pass-2026x"}); print("old password after reset:", st, r.get("code") if isinstance(r, dict) else r)
print("USER", u["id"], email)
