from pypdf import PdfReader
secrets=['zzcanarybmvejw77a','gulfsteel','Gulf Steel','الخليج']
def text(f): return ''.join(p.extract_text() or '' for p in PdfReader(f).pages)
t=text('/d/control-b.pdf')
print('control (B exports its own canary): contains canary =', 'zzcanarybmvejw77a' in t)
ta=text('/d/1.pdf'); print('control (A full users pdf has alnoor):', 'alnoor' in ta)
n=0;leaks=0
for line in open('/d/index.tsv'):
    f,st,u=line.rstrip('\n').split('\t'); f=f.replace('/tmp/claude-1000/-home-shan-ERP-PROJECT-2/0f5b5ee2-6ce0-400e-ad35-b5cf3fd0b57e/scratchpad/c-p03-r12-pdf','/d'); n+=1
    try: txt=text(f)
    except Exception as e: txt=''; print('not pdf',st,u)
    hit=[s for s in secrets if s in txt]
    if hit: leaks+=1; print('LEAK',st,u,hit)
print('pdfs checked',n,'leaks',leaks)
