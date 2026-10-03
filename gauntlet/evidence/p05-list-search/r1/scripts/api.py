import json, urllib.request, urllib.error, time, sys
B='http://localhost:20550'
_tok={}
def login(email, pw='Demo-Pass-2026'):
    if email in _tok: return _tok[email]
    r=req('POST','/api/auth/sign-in',{'email':email,'password':pw,'issueToken':True},None)
    _tok[email]=r[1]['token']; return _tok[email]
def req(method,path,body=None,who='admin@alnoor.example',headers=None):
    h={'Content-Type':'application/json','X-Erp-Request':'1'}
    if who: h['Authorization']='Bearer '+login(who)
    if headers: h.update(headers)
    data=None if body is None else json.dumps(body).encode()
    rq=urllib.request.Request(B+path,data=data,method=method,headers=h)
    t=time.time()
    try:
        with urllib.request.urlopen(rq) as r:
            txt=r.read().decode(); st=r.status; hd=dict(r.headers)
    except urllib.error.HTTPError as e:
        txt=e.read().decode(); st=e.code; hd=dict(e.headers)
    el=time.time()-t
    try: j=json.loads(txt) if txt else None
    except Exception: j=txt
    return st,j,el,hd
