import json, urllib.request, http.cookiejar, sys
J=http.cookiejar.CookieJar(); O=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(J))
def call(path, params):
    r=O.open(urllib.request.Request("http://localhost:8069"+path, json.dumps({"jsonrpc":"2.0","method":"call","params":params}).encode(), {"Content-Type":"application/json"}))
    d=json.load(r)
    if 'error' in d: raise Exception(json.dumps(d['error'])[:500])
    return d['result']
call("/web/session/authenticate", {"db":"reference","login":"admin","password":"admin"})
def kw(model, method, args, kwargs={}):
    return call("/web/dataset/call_kw", {"model":model,"method":method,"args":args,"kwargs":kwargs})
if __name__=="__main__":
    v=kw("res.users","get_views",[[[False,"search"],[False,"list"]]],{})
    print(v['views']['search']['arch'][:3000])
    print(v['views']['list']['arch'][:1500])
    act=kw("ir.actions.act_window","search_read",[[["res_model","=","res.users"]]],{"fields":["name","domain","context"],"limit":5})
    print(act)
