import sys, json, urllib.request
sys.argv=[sys.argv[0]]
exec(open('c-api-attack.py').read().split('A = Client')[0])
A = Client("admin.ar@alnoor.example")
for fmt in ["pdf","xlsx","csv"]:
    r = urllib.request.Request(BASE+f"/api/reports/lists/numbering.sequences?format={fmt}&language=ar", headers={"X-Erp-Request":"1"})
    with A.op.open(r) as resp:
        data = resp.read(); log(fmt, resp.status, resp.headers.get("Content-Type"), len(data))
        if fmt=="csv": log(data.decode("utf-8-sig")[:300].replace("\n"," | "))
        if fmt=="pdf": open("c-sequences-ar.pdf","wb").write(data)
