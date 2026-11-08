import json, urllib.request, urllib.error, sys
B='http://localhost:21050'
def call(method, path, tok=None, body=None, headers=None):
    h={'Content-Type':'application/json','X-Erp-Request':'1'}
    if tok: h['Authorization']='Bearer '+tok
    if headers: h.update(headers)
    req=urllib.request.Request(B+path, data=json.dumps(body).encode() if body is not None else None, headers=h, method=method)
    try:
        r=urllib.request.urlopen(req, timeout=120); raw=r.read(); return r.status, raw
    except urllib.error.HTTPError as e: return e.code, e.read()
def signin(email):
    s,raw=call('POST','/api/auth/sign-in',body={'email':email,'password':'Demo-Pass-2026','issueToken':True})
    d=json.loads(raw); return d.get('token') or d.get('accessToken') or d
def seqs(tok, q=''):
    s,raw=call('GET','/api/numbering/sequences?take=500'+q,tok)
    d=json.loads(raw) if s==200 else raw
    return s,d
A=signin('admin@alnoor.example'); Bt=signin('admin@gulfsteel.example')
if not isinstance(A,str): print('signin A', A); sys.exit(1)
sa,da=seqs(A); sb,db=seqs(Bt)
rowsA=da['rows'] if isinstance(da,dict) and 'rows' in da else da.get('items',da)
rowsB=db['rows'] if isinstance(db,dict) and 'rows' in db else db.get('items',db)
print('A sees',len(rowsA),'B sees',len(rowsB))
idsA={r['id'] for r in rowsA}; idsB={r['id'] for r in rowsB}
print('overlap ids', len(idsA&idsB))
bTax=[r for r in rowsB if r['documentType']=='sales.invoice'][0]
print('B tax invoice', bTax['id'], bTax['prefix'], bTax['companyCode'], bTax.get('version'))
s,raw=call('GET','/api/numbering/sequences/'+bTax['id'],A); print('A GET B seq ->',s)
s,raw=call('PUT','/api/numbering/sequences/'+bTax['id'],A,body={'prefix':'HACK-{YYYY}-','suffix':'','padding':5,'reset':'yearly','gapless':True,'perBranch':False,'version':bTax.get('version')}); print('A PUT B seq ->',s, raw[:200])
s,raw=call('GET','/api/numbering/sequences/'+bTax['id'],Bt); print('B own seq after attack', s, json.loads(raw)['prefix'])
for q in ['&companyId='+bTax['companyId'],'&tenantId=x','&search='+bTax['companyCode'],"&filter=companyCode eq '"+bTax['companyCode']+"'","&filter=companyId eq '"+bTax['companyId']+"'"]:
    s,d=seqs(A,q.replace(' ','%20').replace("'","%27")); r=(d.get('rows') or d.get('items') or []) if isinstance(d,dict) else []
    print('A list',q,'->',s,len(r),'B rows:',sum(1 for x in r if x['id'] in idsB))
for hk in ['X-Tenant','X-Tenant-Id','X-Company','X-Company-Id','X-Erp-Company','X-Erp-Tenant']:
    s,raw=call('GET','/api/numbering/sequences/'+bTax['id'],A,headers={hk:bTax['companyId']}); print('header',hk,'->',s)
for fmt in ['csv','pdf','xlsx']:
    s,raw=call('GET','/api/reports/lists/numbering.sequences?format=%s&companyId=%s'%(fmt,bTax['companyId']),A)
    leak=any(x.encode() in raw for x in [bTax['companyCode'],bTax['id']])
    print('export',fmt,s,len(raw),'contains B marker:',leak)
s,raw=call('GET','/api/lists/numbering.sequences/views',A); print('views',s,raw[:150])
# viewer / noaccess / anon
V=signin('viewer@alnoor.example'); N=signin('noaccess@alnoor.example')
aTax=[r for r in rowsA if r['documentType']=='purchasing.order'][0]
for nm,t in [('viewer',V),('noaccess',N),('anon',None)]:
    out=[]
    out.append(call('GET','/api/numbering/sequences',t)[0])
    out.append(call('GET','/api/numbering/sequences/'+aTax['id'],t)[0])
    out.append(call('PUT','/api/numbering/sequences/'+aTax['id'],t,body={'prefix':aTax['prefix'],'suffix':aTax['suffix'],'padding':aTax['padding'],'reset':aTax['reset'],'gapless':aTax['gapless'],'perBranch':aTax['perBranch'],'version':aTax.get('version')})[0])
    out.append(call('GET','/api/reports/lists/numbering.sequences?format=csv',t)[0])
    print(nm,'list/get/put/export ->',out)
