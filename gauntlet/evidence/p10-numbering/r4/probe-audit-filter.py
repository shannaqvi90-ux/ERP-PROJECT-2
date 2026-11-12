import json, sys, time, urllib.request, urllib.error
BASE = "http://localhost:21050"
def signin(email, ws):
    rq = urllib.request.Request(BASE + "/api/auth/sign-in", data=json.dumps({"email": email, "password": "Demo-Pass-2026", "workspace": ws, "issueToken": True}).encode(), method="POST", headers={"Content-Type": "application/json", "X-Erp-Request": "1"})
    return json.loads(urllib.request.urlopen(rq).read())["token"]
def get(tok, path):
    t = time.time()
    rq = urllib.request.Request(BASE + path, headers={"Authorization": "Bearer " + tok, "Accept": "application/json"})
    try:
        with urllib.request.urlopen(rq, timeout=300) as r: b = r.read(); s = r.status
    except urllib.error.HTTPError as e: b = e.read(); s = e.code
    except Exception as e: b = str(e).encode(); s = "ERR"
    return s, time.time() - t, b
A = signin("admin@alnoor.example", "alnoor.example"); B = signin("admin@gulfsteel.example", "gulfsteel.example")
for who, tok, q in [("B", B, "take=50&filter=fields%20eq%20%27city%27"), ("A", A, "take=50&filter=fields%20eq%20%27columns%27"),
                    ("B", B, "take=100&filter=fields%20eq%20%27email%27&skip=205010"), ("B", B, "take=50&filter=fields%20eq%20%27reset%27"),
                    ("A", A, "take=50&filter=fields%20eq%20%27prefix%27"), ("A", A, "take=100&filter=fields%20eq%20%27status%27&skip=51250"),
                    ("B", B, "take=50"), ("A", A, "take=20002"), ("A", A, "take=30017")]:
    s, dt, b = get(tok, "/api/audit/log?" + q)
    try: j = json.loads(b); info = f"total={j.get('total')} items={len(j.get('items', []))}" if s == 200 else b[:300].decode(errors='replace')
    except Exception: info = b[:200]
    print(f"{who} /api/audit/log?{q} -> {s} in {dt:.1f}s {info}", flush=True)
