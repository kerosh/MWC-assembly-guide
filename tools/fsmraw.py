# usage: fsmraw.py scene <exact GO path> <fsmName> > out   (raw YAML of that FSM)
import sys,re
scene,gpath,fname=sys.argv[1:4]
PM='93b76d6ca32738635d1a0d8b86cbf7a3'
names={}; parent={}; tr_go={}; go_tr={}; fsms={}
typ=None;fid=None;buf=[]
def flush():
    if not buf: return
    t='\n'.join(buf)
    if typ==114 and PM in t:
        g=re.search(r'm_GameObject: \{fileID: (\d+)\}',t)
        fn=re.search(r'\n    name: (.*)',t)
        if g and fn and fn.group(1)==fname: fsms.setdefault(g.group(1),[]).append(t)
    elif typ in (4,224):
        g=re.search(r'm_GameObject: \{fileID: (\d+)\}',t); p=re.search(r'm_Father: \{fileID: (\d+)\}',t)
        if g: tr_go[fid]=g.group(1); go_tr[g.group(1)]=fid; parent[fid]=p.group(1) if p else '0'
with open(scene,encoding='utf-8',errors='replace') as f:
    for line in f:
        if line.startswith('--- !u!'):
            flush(); buf=[]; m=re.match(r'--- !u!(\d+) &(-?\d+)',line); typ=int(m.group(1)); fid=m.group(2); continue
        if typ==1:
            if line.startswith('  m_Name:'): names[fid]=line[9:].strip()
        elif typ in (114,4,224): buf.append(line.rstrip('\n'))
    flush()
def path(g):
    p=[]; t=go_tr.get(g); n=0
    while t and t!='0' and n<40:
        p.append(names.get(tr_go.get(t),'?')); t=parent.get(t); n+=1
    return '/'.join(reversed(p))
for g,ts in fsms.items():
    if path(g)==gpath:
        for t in ts: print(t)
