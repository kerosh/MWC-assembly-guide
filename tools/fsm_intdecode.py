# usage: python3 fsm_intdecode.py <raw FSM yaml from fsmraw.py> [ActionName ...]
# Decodes literal FsmInt parameters of IntCompare / IntSwitch actions from PlayMaker's serialized byteData
# (FsmInt layout: int32 little-endian value, then a useVariable byte, then the variable name).
import re, struct, sys
t = open(sys.argv[1], encoding='utf-8', errors='replace').read()
want = set(sys.argv[2:]) or {'IntCompare', 'IntSwitch'}
def i32s(h):
    b = bytes.fromhex(h)
    return [struct.unpack('<i', b[i:i + 4])[0] for i in range(0, len(b), 4)]
for st in re.split(r'\n    - name: ', t)[1:]:
    name = st.split('\n')[0]
    m = re.search(r'byteData: ([0-9a-f]*)', st)
    if not m:
        continue
    bd = bytes.fromhex(m.group(1))
    types = i32s(re.search(r'paramDataType: ([0-9a-f]*)', st).group(1))
    pos = i32s(re.search(r'paramDataPos: ([0-9a-f]*)', st).group(1))
    size = i32s(re.search(r'paramByteDataSize: ([0-9a-f]*)', st).group(1))
    starts = i32s(re.search(r'actionStartIndex: ([0-9a-f]*)', st).group(1))
    acts = re.findall(r'Actions\.(\w+)', st)
    out = []
    for ai, a in enumerate(acts):
        if a not in want:
            continue
        s = starts[ai]
        e = starts[ai + 1] if ai + 1 < len(starts) else len(types)
        vals = []
        for k in range(s, e):
            if size[k] <= 0:
                continue
            b = bd[pos[k]:pos[k] + size[k]]
            if types[k] == 16 and len(b) >= 5:   # FsmInt
                v = struct.unpack('<i', b[0:4])[0]
                vals.append(('var ' + b[5:].decode('latin1')) if b[4] else v)
            else:
                vals.append(b.decode('latin1'))
        out.append((a, vals))
    if out:
        print(name, out)
