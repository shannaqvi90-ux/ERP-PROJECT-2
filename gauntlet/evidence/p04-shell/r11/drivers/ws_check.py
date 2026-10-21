exec(open('isolation_probe_base.py').read().split("spec = json.loads")[0])
import json
for name, r in [('B', reqB), ('A', reqA)]:
    s, h, b = r('GET', '/api/tenancy/workspace'); print(name, s, b.decode()[:1500]); print()
