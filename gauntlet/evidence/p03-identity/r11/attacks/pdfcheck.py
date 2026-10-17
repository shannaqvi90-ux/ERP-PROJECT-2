from pypdf import PdfReader
secrets=['zzcanarybmvcironr','gulfsteel','Gulf Steel','الخليج']
def text(f): return ''.join(p.extract_text() or '' for p in PdfReader(f).pages)
t=text('/d/control-b.pdf')
print('control (B exports its own canary): contains canary =', 'zzcanarybmvcironr' in t)
ta=text('/d/1.pdf'); print('control (A full users pdf has alnoor):', 'alnoor' in ta)
n=0;leaks=0
for line in open('/d/index.tsv'):
    f,st,u=line.rstrip('\n').split('\t'); f=f.replace('/tmp/c-p03-r11-pdf','/d'); n+=1
    try: txt=text(f)
    except Exception as e: txt=''; print('not pdf',st,u)
    hit=[s for s in secrets if s in txt]
    if hit: leaks+=1; print('LEAK',st,u,hit)
print('pdfs checked',n,'leaks',leaks)
