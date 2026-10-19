# usage: python3 fsmdump.py GAME.unity <path-prefix-regex> [maxchars]
# dumps every PlayMakerFSM under GameObjects whose full path matches the regex:
# fsm name, variables, states -> actions + string/object refs + transitions
import sys,re,pickle,os
scene=sys.argv[1]; pat=re.compile(sys.argv[2]); maxc=int(sys.argv[3]) if len(sys.argv)>3 else 6000
PM='93b76d6ca32738635d1a0d8b86cbf7a3'
names={}; parent={}; tr_go={}; go_tr={}; fsms={}
typ=None;fid=None;buf=[]
def flush():
    if not buf: return
    t='\n'.join(buf)
    if typ==114 and PM in t:
        g=re.search(r'm_GameObject: \{fileID: (\d+)\}',t)
        if g: fsms.setdefault(g.group(1),[]).append(t)
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
def summarize(t):
    out=[]
    fn=re.search(r'\n    name: (.*)',t); out.append('FSM '+(fn.group(1) if fn else '?'))
    parts=re.split(r'\n    - name: ',t)
    for st in parts[1:]:
        sname=st.split('\n')[0]
        if sname in ('FINISHED',) : continue
        acts=re.findall(r'HutongGames\.PlayMaker\.Actions\.(\w+)',st)
        if not acts and 'toState' not in st: continue
        trans=re.findall(r'name: (\S[^\n]*)\n\s+isGlobal: \d\n\s+toState: (.*)',st)
        if not trans: trans=re.findall(r'toState: (.*)',st)
        strs=[s for s in re.findall(r'\n\s+(?:value|stringValue): ([^\n{]+)',st) if s.strip() not in ('0','1','')]
        refs=[names.get(x,x) for x in re.findall(r'value: \{fileID: (\d+)\}',st) if x!='0']
        vars_=re.findall(r'\n\s+name: ([A-Za-z_][^\n]*)',st)
        out.append('  [%s] acts=%s'%(sname,','.join(acts)))
        if strs: out.append('     strs=%s'%strs[:40])
        if refs: out.append('     refs=%s'%refs[:20])
        if vars_: out.append('     vars=%s'%sorted(set(vars_))[:40])
        if trans: out.append('     -> %s'%(trans,))
    return '\n'.join(out)
for g,ts in fsms.items():
    p=path(g)
    if pat.search(p):
        for t in ts:
            print('=== '+p); print(summarize(t)[:maxc])
