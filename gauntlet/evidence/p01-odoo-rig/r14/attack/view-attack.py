# Critic p01 r14: tenant B makes private and shared saved views (a canary in the name and search); tenant A tries to read, list, change and delete them.
import json, urllib.request
BASE='http://localhost:20150'
def req(m,p,body=None,tok=None):
    h={'Content-Type':'application/json','X-Erp-Request':'1'}
    if tok: h['Authorization']='Bearer '+tok
    r=urllib.request.Request(BASE+p,method=m,data=None if body is None else json.dumps(body).encode(),headers=h)
    try:
        with urllib.request.urlopen(r,timeout=60) as x: return x.status,x.read().decode()
    except urllib.error.HTTPError as e: return e.code,e.read().decode()
tok=lambda e: json.loads(req('POST','/api/auth/sign-in',{'email':e,'password':'Demo-Pass-2026','issueToken':True})[1])['token']
B=tok('admin@gulfsteel.example'); A=tok('admin@alnoor.example')
made=[]
for lst in ['identity.users','identity.roles','tenancy.companies','tenancy.branches']:
    for kind in ['views','shared-views']:
        s,b=req('POST',f'/api/lists/{lst}/{kind}',{'name':'GSR14CANARY '+kind,'columns':['displayName'] if lst=='identity.users' else ['name'],'search':'GSR14CANARY'},B)
        print('B makes',lst,kind,s,(json.loads(b).get('id') if s<300 else b[:160]))
        if s<300: made.append((lst,kind,json.loads(b)))
leaks=0
for lst,kind,v in made:
    for m,p,body in [('GET',f'/api/lists/{lst}/{kind}/{v["id"]}',None),('PUT',f'/api/lists/{lst}/{kind}/{v["id"]}',{'name':'A was here','columns':v.get('columns',[]),'version':v.get('version',1)}),('DELETE',f'/api/lists/{lst}/{kind}/{v["id"]}',None),('GET',f'/api/lists/{lst}/{kind}',None),('GET',f'/api/lists/{lst}/views',None)]:
        s,b=req(m,p,body,A)
        bad=('GSR14CANARY' in b and s<400) or (m in ('PUT','DELETE') and s<300)
        leaks+=bad
        print('A',m,p,s,'LEAK' if bad else 'ok')
s,b=req('GET','/api/lists/identity.users/views',None,B); print('B still sees its own views:', b.count('GSR14CANARY'), 'names intact:', 'A was here' not in b)
print('leaks', leaks)
