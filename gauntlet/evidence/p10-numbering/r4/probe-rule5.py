import json, subprocess, sys
exec(open('/home/shan/evidence-staging/p10-numbering/r4/api-attack.py').read().split("A=signin")[0])
D='c-p10-numbering-r4-db-1'; T='0190a000-0000-7000-8000-000000000001'
def sql(q): return subprocess.run(['docker','exec',D,'psql','-U','postgres','-d','erp','-At','-c',q],capture_output=True,text=True)
A=signin('admin@alnoor.example')
def get(i): return json.loads(call('GET','/api/numbering/sequences/'+i,A)[1])
def put(i, **kw):
    s=get(i); body={k:s[k] for k in ['prefix','suffix','padding','reset','gapless','perBranch']}; body['version']=s['version']; body.update(kw)
    st,raw=call('PUT','/api/numbering/sequences/'+i,A,body=body); return st, raw.decode()[:300]
def issue(seq, comp, nums):
    for (num, d, py, pm, v) in nums:
        r=sql(f"insert into numbering.issued_numbers (id,company_id,sequence_id,document_type,period_year,period_month,value,number,document_date,gapless,tenant_id) values (gen_random_uuid(),'{comp}','{seq}','sales.creditNote',{py},{pm},{v},'{num}','{d}',true,'{T}')")
        if r.returncode or 'ERROR' in r.stderr: print('insert err', r.stderr)
def counter(seq, comp, py, pm, nv):
    r=sql(f"insert into numbering.counters (id,company_id,sequence_id,period_year,period_month,next_value,tenant_id) values (gen_random_uuid(),'{comp}','{seq}',{py},{pm},{nv},'{T}')"); print('counter', r.stderr.strip() or 'ok')
# (a) yearly CN-{YYYY}-{MM}- with October numbers, then switch to monthly
SA='01a1b9ab-cc06-7e22-9eac-c4a4a70818fb'; CA='018dca08-f081-766c-92a4-b5dabeca9b63'
print('(a) set prefix CN-{YYYY}-{MM}- yearly:', put(SA, prefix='CN-{YYYY}-{MM}-', reset='yearly'))
issue(SA, CA, [(f'CN-2026-10-0000{v}','2026-10-15',2026,0,v) for v in (1,2,3)])
r=sql(f"update numbering.counters set next_value=4 where sequence_id='{SA}' and period_year=2026 and period_month=0"); print('counter upd', r.stdout, r.stderr)
if 'UPDATE 0' in (r.stdout+r.stderr) or sql(f"select count(*) from numbering.counters where sequence_id='{SA}'").stdout.strip()=='0': counter(SA, CA, 2026, 0, 4)
print('  preview:', get(SA)['nextNumber'])
print('(a) switch to monthly ->', put(SA, reset='monthly'))
# (b) never CN-{YYYY}- with 2025 numbers, then switch to yearly
SB='01a1b9ab-cc11-7cf2-a6e6-1a47540b9f21'; CB='018dca08-f085-7181-b044-1ee2879acf5b'
print('(b) set reset never (prefix CN-{YYYY}-):', put(SB, reset='never'))
issue(SB, CB, [(f'CN-2025-0000{v}','2025-12-20',0,0,v) for v in (1,2,3)])
counter(SB, CB, 0, 0, 4)
print('  preview:', get(SB)['nextNumber'])
print('(b) switch to yearly ->', put(SB, reset='yearly'))
# (c) control: a change that cannot repeat a number is accepted (padding up on (b))
print('(c) control padding 6 on (b) ->', put(SB, padding=6))
