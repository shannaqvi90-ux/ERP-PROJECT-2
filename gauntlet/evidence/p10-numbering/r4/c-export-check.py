import sys, io, zipfile, re, json
sys.argv=[sys.argv[0]]
exec(open('c-api-attack.py').read().split('A = Client')[0])
import urllib.request
A = Client("admin@alnoor.example"); B = Client("admin@gulfsteel.example")
b = json.loads(B.req("GET","/api/numbering/sequences?take=500")[1])["items"]
marks = {i["companyCode"] for i in b} | {i["companyId"] for i in b} | {"Gulf", "gulfsteel"}
for fmt in ["xlsx","pdf"]:
  r = urllib.request.Request(BASE+f"/api/reports/lists/numbering.sequences?format={fmt}&columns=nameEn,companyId,nextNumber,prefix", headers={"X-Erp-Request":"1"})
  data = A.op.open(r).read()
  if fmt=="xlsx":
    z=zipfile.ZipFile(io.BytesIO(data)); text=" ".join(z.read(n).decode('utf-8','replace') for n in z.namelist())
  else:
    open('/tmp/claude-1000/c-p10.pdf','wb').write(data); import subprocess
    import zlib; text=' '.join(zlib.decompress(m).decode('latin1','replace') for m in re.findall(rb'stream\r?\n(.*?)endstream',data,re.S) if m[:1]==b'x' or True for _ in [0] if (lambda: True)()) if False else data.decode('latin1')
  print(fmt, len(data), "A-markers present:", "ALN" in text or "alnoor" in text.lower() or "Al Noor" in text, "B leak:", [m for m in marks if m in text])
