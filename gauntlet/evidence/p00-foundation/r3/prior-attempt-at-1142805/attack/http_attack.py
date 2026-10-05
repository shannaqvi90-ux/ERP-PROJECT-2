#!/usr/bin/env python3
"""Black-box tenant-isolation probe (critic, p00-foundation r3).

Signed in as tenant A (alnoor: admin cookie, admin bearer, viewer, noaccess) and anonymously,
send tenant B's (gulfsteel) ids, e-mails, names and codes through every route, query and body
field the OpenAPI document lists, plus common tenant-switch headers. Every response body and
header is searched for B's markers. B's database checksum is compared before and after
by the caller (see run.sh)."""
import json, sys, urllib.request, urllib.error, http.cookiejar, itertools, re

BASE = sys.argv[1]
MARKERS = json.load(open(sys.argv[2]))  # {"ids": [...], "strings": [...], "tenantId": "...", "code": "gulfsteel"}
PASSWORD = "Demo-Pass-2026"

def opener():
    jar = http.cookiejar.CookieJar()
    return urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))

def call(op, method, path, body=None, headers=None, bearer=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(BASE + path, data=data, method=method)
    req.add_header("X-Erp-Request", "1")
    if data is not None:
        req.add_header("Content-Type", "application/json")
    if bearer:
        req.add_header("Authorization", "Bearer " + bearer)
    for k, v in (headers or {}).items():
        req.add_header(k, v)
    try:
        with op.open(req, timeout=60) as r:
            return r.status, r.read().decode("utf-8", "replace"), str(r.headers)
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace"), str(e.headers)

def sign_in(email):
    op = opener()
    s, t, _ = call(op, "POST", "/api/auth/sign-in", {"email": email, "password": PASSWORD})
    if s != 200:
        raise SystemExit(f"sign-in {email}: {s} {t[:200]}")
    tok = None
    try:
        tok = json.loads(t).get("token")
    except Exception:
        pass
    return op, tok

ids = MARKERS["ids"]
strings = MARKERS["strings"]
tenant_b = MARKERS["tenantId"]
code_b = MARKERS["code"]
needles = [x.lower() for x in ids + strings if len(x) >= 6]

def leak(text):
    low = text.lower()
    for n in needles:
        if n in low:
            return n
    return None

doc = json.load(urllib.request.urlopen(BASE + "/api/openapi/v1.json"))
ops = []
for path, item in doc["paths"].items():
    for method, spec in item.items():
        if method.upper() not in ("GET", "POST", "PUT", "PATCH", "DELETE"):
            continue
        ops.append((method.upper(), path, spec))

actors = []
admin_op, admin_tok = sign_in("admin@alnoor.example")
actors.append(("A admin cookie", admin_op, None))
bop, btok = sign_in("admin@alnoor.example")
actors.append(("A admin bearer", opener(), btok or None))
actors.append(("A viewer", sign_in("viewer@alnoor.example")[0], None))
actors.append(("A noaccess", sign_in("noaccess@alnoor.example")[0], None))
actors.append(("anonymous", opener(), None))

switch_headers = ["X-Tenant-Id", "X-Tenant", "Tenant-Id", "X-Company-Id", "X-Erp-Workspace", "X-Workspace",
                  "X-Erp-Tenant", "X-Forwarded-Host", "X-Organization-Id"]

def body_for(spec, value):
    rb = spec.get("requestBody", {}).get("content", {}).get("application/json", {}).get("schema")
    if not rb:
        return None
    if "$ref" in rb:
        rb = doc["components"]["schemas"][rb["$ref"].split("/")[-1]]
    out = {}
    for name, p in (rb.get("properties") or {}).items():
        t = p.get("type")
        if isinstance(t, list):
            t = [x for x in t if x != "null"][0] if [x for x in t if x != "null"] else None
        if p.get("format") == "uuid":
            out[name] = value if re.fullmatch(r"[0-9a-f-]{36}", value) else tenant_b
        elif t == "array":
            out[name] = [value]
        elif t == "string":
            out[name] = value
        elif t == "integer":
            out[name] = 0
        elif t == "boolean":
            out[name] = True
    return out

requests = 0
leaks = []
errors = []
values = list(dict.fromkeys(ids[-6:] + ids[:6] + strings[:8] + strings[-4:] + [tenant_b, code_b]))
SKIP = {"/api/auth/sign-out"}
for method, path, spec in ops:
    if path in SKIP:
        continue
    params = spec.get("parameters", [])
    route_params = re.findall(r"{([^}:]+)", path)
    query_params = [p["name"] for p in params if p.get("in") == "query"] + ["tenantId", "tenant", "workspace", "companyId"]
    for actor_name, op, tok in actors:
        for v in values:
            p = path
            for rp in route_params:
                p = re.sub(r"{" + rp + r"[^}]*}", urllib.parse.quote(v, safe=""), p)
            reqs = [(p, None)]
            if method == "GET":
                for q in query_params:
                    reqs.append((p + ("&" if "?" in p else "?") + urllib.parse.urlencode({q: v}), None))
            body = body_for(spec, v) if method in ("POST", "PUT", "PATCH") else None
            for rp_path, _ in reqs:
                s, t, h = call(op, method, rp_path, body, bearer=tok)
                requests += 1
                if s >= 500:
                    errors.append(f"{actor_name} {method} {rp_path} -> {s}")
                m = leak(t) or leak(h)
                if m and s < 400:
                    leaks.append(f"{actor_name} {method} {rp_path} -> {s}: marker {m}")
                elif m:
                    leaks.append(f"{actor_name} {method} {rp_path} -> {s} (error body): marker {m}")
        # tenant-switch headers, once per endpoint per actor
        p = path
        for rp in route_params:
            p = re.sub(r"{" + rp + r"[^}]*}", ids[0], p)
        for hname in switch_headers:
            for hv in (tenant_b, code_b):
                s, t, h = call(op, method, p, body_for(spec, ids[0]) if method in ("POST", "PUT", "PATCH") else None,
                               headers={hname: hv}, bearer=tok)
                requests += 1
                m = leak(t) or leak(h)
                if m:
                    leaks.append(f"{actor_name} {method} {p} [{hname}: {hv}] -> {s}: marker {m}")
                if s >= 500:
                    errors.append(f"{actor_name} {method} {p} [{hname}] -> {s}")

# Sign in naming tenant B's workspace with A's credentials
s, t, _ = call(opener(), "POST", "/api/auth/sign-in", {"email": "admin@alnoor.example", "password": PASSWORD, "workspace": code_b, "tenantId": tenant_b})
requests += 1
if tenant_b in t:
    leaks.append("sign-in with workspace=B returned B's tenant id")

print(json.dumps({"operations": len(ops), "requests": requests, "leaks": leaks, "server_errors": errors[:50],
                  "server_error_count": len(errors)}, indent=1))
