import json, urllib.request
BASE='http://localhost:20150'
def req(m,p,body=None,tok=None):
    h={'Content-Type':'application/json','X-Erp-Request':'1'}
    if tok: h['Authorization']='Bearer '+tok
    r=urllib.request.Request(BASE+p,method=m,data=None if body is None else json.dumps(body).encode(),headers=h)
    try:
        with urllib.request.urlopen(r,timeout=60) as x: return x.status
    except urllib.error.HTTPError as e: return e.code
tok=lambda e: json.loads(urllib.request.urlopen(urllib.request.Request(BASE+'/api/auth/sign-in',method='POST',data=json.dumps({'email':e,'password':'Demo-Pass-2026','issueToken':True}).encode(),headers={'Content-Type':'application/json','X-Erp-Request':'1'})).read())['token']
for who in ['noaccess@alnoor.example','viewer@alnoor.example','admin@alnoor.example',None]:
    t = tok(who) if who else None
    out = {p: req('GET',p,None,t) for p in ['/api/identity/users','/api/identity/roles','/api/tenancy/companies','/api/tenancy/branches','/api/audit/entries']}
    out['POST users'] = req('POST','/api/identity/users',{'email':'g2probe@alnoor.example','displayName':'g2','language':'en','roleIds':[]},t) if who!='admin@alnoor.example' else 'skipped'
    print(who or 'anonymous', out)
