import json, urllib.request, http.cookiejar, uuid, sys
B = "http://localhost:20350"
class C:
    def __init__(s, email=None, pw="Demo-Pass-2026", workspace=None):
        s.cj = http.cookiejar.CookieJar(); s.op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(s.cj))
        if email:
            body = {"email": email, "password": pw}
            if workspace: body["workspace"] = workspace
            st, r = s.req("POST", "/api/auth/sign-in", body)
            s.signin = (st, r)
    def req(s, m, path, body=None):
        data = json.dumps(body).encode() if body is not None else None
        rq = urllib.request.Request(B + path, data=data, method=m, headers={"X-Erp-Request": "1", "Content-Type": "application/json", "Accept": "application/json"})
        try:
            with s.op.open(rq, timeout=120) as resp:
                t = resp.read().decode(); st = resp.status
        except urllib.error.HTTPError as e:
            t = e.read().decode(); st = e.code
        try: return st, json.loads(t) if t else None
        except Exception: return st, t
    def get(s, p): return s.req("GET", p)
    def post(s, p, b=None): return s.req("POST", p, b if b is not None else {})
    def put(s, p, b): return s.req("PUT", p, b)
    def delete(s, p): return s.req("DELETE", p)
def tag(): return uuid.uuid4().hex[:6]
def show(label, r):
    st, b = r; t = json.dumps(b, ensure_ascii=False) if not isinstance(b, str) else b
    print(f"{label}: [{st}] {t[:400]}"); sys.stdout.flush(); return r
