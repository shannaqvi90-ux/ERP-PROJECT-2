import json,subprocess,time,sys,urllib.request,http.cookiejar
B='http://localhost:21050'
SESS=open('session.txt').read().strip()  # an erp_session cookie of an administrator
op=urllib.request.build_opener()
def req(m,p,body=None):
    r=urllib.request.Request(B+p,method=m,data=json.dumps(body).encode() if body is not None else None,headers={'X-Erp-Request':'1','content-type':'application/json','Cookie':'erp_session='+SESS})
    t=time.time()
    try:
        with op.open(r,timeout=600) as f: return f.status,json.loads(f.read() or b'null'),time.time()-t
    except urllib.error.HTTPError as e: return e.code,json.loads(e.read() or b'null'),time.time()-t
mode=sys.argv[1]
CO='018dca08-f081-7e9f-adb3-c1279a812d6e'
if mode=='branches':
    n=int(sys.argv[2]); start=int(sys.argv[3]); ok=0
    for i in range(start,start+n):
        s,b,_=req('POST','/api/tenancy/branches',{"companyId":CO,"code":f"CRB{i:04d}","nameEn":f"Critic branch {i}","nameAr":f"فرع {i}","addressLine1":"","addressLine2":"","city":"Dubai","emirate":"dubai","poBox":"","country":"AE","addressAr":"","phone":"","email":"","isActive":True,"version":0})
        if s in (200,201): ok+=1
        elif i==start: print(s,json.dumps(b)[:400])
    print('created',ok)
else:
    s,lst,_=req('GET','/api/numbering/sequences?take=200')
    for typ in ['sales.invoice','purchasing.order']:
        row=[i for i in lst['items'] if i['documentType']==typ and i['companyCode']=='ALN-DXB'][0]
        s,rec,_=req('GET','/api/numbering/sequences/'+row['id'])
        pre=rec['prefix']
        s,res,t=req('PUT','/api/numbering/sequences/'+row['id'],{"prefix":"X-{BRANCH}-{YYYY}-","suffix":"","padding":5,"reset":"yearly","gapless":True,"perBranch":True,"version":rec['version']})
        print(typ,'branches',len(rec['branches']),'PUT perBranch+{BRANCH}:',s,f'{t:.2f}s', (json.dumps(res)[:160] if s!=200 else ''))
        if s==200:
            s2,res2,t2=req('PUT','/api/numbering/sequences/'+row['id'],{"prefix":pre,"suffix":"","padding":5,"reset":"yearly","gapless":True,"perBranch":False,"version":res['version']})
            print('  restore',s2,f'{t2:.2f}s',(json.dumps(res2)[:200] if s2!=200 else ''))
