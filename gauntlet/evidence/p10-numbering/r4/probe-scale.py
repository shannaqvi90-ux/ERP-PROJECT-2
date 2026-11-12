import json, time
exec(open('/home/shan/evidence-staging/p10-numbering/r4/api-attack.py').read().split("A=signin")[0])
A=signin('admin@alnoor.example')
S='01a1b9ab-cc1b-7c20-a1bf-3b1d5c1c2d3b'
def get(i): return json.loads(call('GET','/api/numbering/sequences/'+i,A)[1])
def put(i, **kw):
    s=get(i); body={k:s[k] for k in ['prefix','suffix','padding','reset','gapless','perBranch']}; body['version']=s['version']; body.update(kw)
    t=time.time(); st,raw=call('PUT','/api/numbering/sequences/'+i,A,body=body); return st, round(time.time()-t,2), raw.decode()[:160]
print('200,000 issued INV-2021..2025 numbers + 2 of 2026 on this sequence')
print('padding 6 :', put(S, padding=6))
print('padding 5 back:', put(S, padding=5))
print('perBranch true + {BRANCH}:', put(S, prefix='INV-{BRANCH}-{YYYY}-', perBranch=True))
print('back:', put(S, prefix='INV-{YYYY}-', perBranch=False))
print('suffix /X:', put(S, suffix='/X'))
print('suffix back:', put(S, suffix=''))
t=time.time(); call('GET','/api/numbering/sequences/'+S,A); print('GET', round(time.time()-t,2))
t=time.time(); call('GET','/api/numbering/sequences?take=50',A); print('LIST', round(time.time()-t,2))
