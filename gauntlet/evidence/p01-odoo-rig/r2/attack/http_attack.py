import json, urllib.request, http.cookiejar, sys
BASE='http://localhost:20150'
B_TENANT='0190a000-0000-7000-8000-000000000002'
B_IDS=sys.argv[1].split(',')   # B user and role ids
def client():
    cj=http.cookiejar.CookieJar(); op=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cj)); return op
def req(op, method, path, body=None, headers=None):
    h={'X-Erp-Request':'1','Content-Type':'application/json'}; h.update(headers or {})
    r=urllib.request.Request(BASE+path, data=json.dumps(body).encode() if body is not None else None, headers=h, method=method)
    try:
        resp=op.open(r); return resp.status, resp.read().decode(), dict(resp.headers)
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode(), dict(e.headers)
A=client(); B=client()
print(req(A,'POST','/api/auth/sign-in',{'email':'admin@alnoor.example','password':'Demo-Pass-2026'})[0])
print(req(B,'POST','/api/auth/sign-in',{'email':'admin@gulfsteel.example','password':'Demo-Pass-2026'})[0])
markers=['gulfsteel', B_TENANT]+B_IDS
n=0; leaks=[]
def judge(label, st, txt, hd):
    global n; n+=1
    blob=txt+json.dumps(hd)
    for m in markers:
        if m.lower() in blob.lower(): leaks.append((label, st, m)); break
# B warms up everything
for p in ['/api/tenancy/tenant','/api/identity/users?search=gulfsteel','/api/identity/roles','/api/auth/session','/api/identity/permissions']+[f'/api/identity/users/{i}' for i in B_IDS]+[f'/api/identity/roles/{i}' for i in B_IDS]:
    req(B,'GET',p)
hdr_variants=[{},{'X-Tenant-Id':B_TENANT},{'X-Tenant':'gulfsteel'},{'Tenant-Id':B_TENANT},{'X-Forwarded-Host':'gulfsteel.example'},{'Cookie':'erp_tenant='+B_TENANT}]
gets=['/api/tenancy/tenant','/api/auth/session','/api/identity/roles','/api/identity/permissions',
 '/api/identity/users?search=gulfsteel','/api/identity/users?search=admin@gulfsteel.example','/api/identity/users?search='+B_TENANT,
 '/api/identity/users?tenantId='+B_TENANT,'/api/identity/roles?tenantId='+B_TENANT,'/api/identity/users?search=tariq.alhashimi.1@gulfsteel.example',
 '/api/identity/users?pageSize=100000','/api/identity/users?sort=email&search=gulf']
gets+= [f'/api/identity/users/{i}' for i in B_IDS]+[f'/api/identity/roles/{i}' for i in B_IDS]
for h in hdr_variants:
    for p in gets:
        st,txt,hd=req(A,'GET',p,headers=h); judge(f'GET {p} {h}',st,txt,hd)
        req(B,'GET','/api/tenancy/tenant')
# writes
for i in B_IDS:
    for m,p,b in [('PUT',f'/api/identity/users/{i}',{'displayName':'pwned','language':'en','roleIds':[],'version':1,'isActive':False}),
                  ('PUT',f'/api/identity/roles/{i}',{'nameEn':'pwned','nameAr':'pwned','permissions':[],'version':1}),
                  ('DELETE',f'/api/identity/roles/{i}',None)]:
        st,txt,hd=req(A,m,p,b); judge(f'{m} {p}',st,txt,hd); print(m,p,st)
st,txt,hd=req(A,'POST','/api/identity/users',{'email':'x.attack@alnoor.example','displayName':'x','language':'en','password':'Demo-Pass-2026x!','roleIds':B_IDS}); judge('POST user with B roles',st,txt,hd); print('POST user B roles',st,txt[:200])
st,txt,hd=req(A,'PUT','/api/tenancy/tenant',{'nameEn':'pwn','nameAr':'pwn','version':0},headers={'X-Tenant-Id':B_TENANT}); print('PUT tenant w/ B header',st)
# B's own view after
st,txt,hd=req(B,'GET','/api/tenancy/tenant'); print('B tenant after:',txt)
print('requests',n,'leaks',len(leaks)); [print(l) for l in leaks[:20]]
