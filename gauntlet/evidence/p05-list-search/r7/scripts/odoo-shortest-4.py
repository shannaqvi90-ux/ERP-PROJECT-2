# Shortest text an Odoo expert can type in the Users search (name/login/email ilike, Internal Users, order name, login)
# so that the target user is (a) the only result, or (b) among the first N rows shown.
import sys, itertools



exec(open(__import__('os').path.join(__import__('os').path.dirname(__import__('os').path.abspath(__file__)),'odoo-rpc.py')).read().split('if __name__')[0])
name, login = "Majid Anil Pillai", "majid.pillai.068311@staff.example"
VISIBLE = int(sys.argv[1]) if len(sys.argv)>1 else 20
cands=set()
for src in (name.lower(),):
    for i in range(len(src)):
        for l in range(1, 12):
            if i+l<=len(src): cands.add(src[i:i+l])
res=[]
for s in sorted(cands, key=lambda x:(len(x),x)):
    dom=[["share","=",False],"|","|",["name","ilike",s],["login","ilike",s],["email","ilike",s]]
    rows=kw("res.users","search_read",[dom],{"fields":["login"],"order":"name, login","limit":VISIBLE})
    rank=next((i+1 for i,r in enumerate(rows) if r['login']==login), None)
    if rank:
        cnt=kw("res.users","search_count",[dom])
        res.append((len(s), s, rank, cnt))
        print(len(s), repr(s), "rank", rank, "of", cnt, flush=True)
    if len(s)>4: break
