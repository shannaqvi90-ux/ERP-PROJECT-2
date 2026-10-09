# Critic p03 r7: an address that two workspaces both hold (tenant A created a user with tenant B's admin address).
import sys; sys.path.insert(0,'.')
from lib import *
show("wrong password, shared address", C().post("/api/auth/sign-in", {"email": "admin.ar@gulfsteel.example", "password": "wrong-Pass-1"}))
show("right password, shared address, no workspace", C().post("/api/auth/sign-in", {"email": "admin.ar@gulfsteel.example", "password": "Demo-Pass-2026"}))
show("wrong password, unshared B address", C().post("/api/auth/sign-in", {"email": "admin@gulfsteel.example", "password": "wrong-Pass-1"}))
show("unknown address", C().post("/api/auth/sign-in", {"email": "nobody.x1@gulfsteel.example", "password": "wrong-Pass-1"}))
