exec(open('isolation_probe_base.py').read().split("ownA = set()")[0])
for p in ['/api/tenancy/workspace','/api/tenancy/workplace']:
    s,h,b=reqB('GET',p); print(p,s,b[:600])
    s,h,b=reqB('GET',p); print(p,s,b[:600])
