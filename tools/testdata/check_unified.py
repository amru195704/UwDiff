# > パス: tools/testdata/check_unified.py
# unified diff が「旧 → 新」を正しく表しているかを、旧・新・差分を先頭から同時に読みながら確かめる（書き出さない・メモリを使わない）。
#   同じ行（文脈と、まとまりの外）は旧と新で同じ、- は旧の行、+ は新の行、最後に旧と新が同時に終わること。
# 使い方: python3 check_unified.py 旧 新 差分   → 合えば 0
import re, sys
old, new, diff = (open(p, 'rb') for p in sys.argv[1:4])
HUNK = re.compile(rb'^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@')
oline = nline = 0
def bad(msg):
    print(f'不一致: {msg}（旧 {oline} 行目・新 {nline} 行目）'); sys.exit(1)
def ro():
    global oline; l = old.readline(); oline += 1; return l
def rn():
    global nline; l = new.readline(); nline += 1; return l
deleted = inserted = hunks = 0
pending = None      # 直前の差分の行（「改行なし」の印を待つ）: (種類, 中身)
def flush():
    global pending, deleted, inserted
    if pending is None: return
    kind, body, nonl = pending
    want = body if nonl else body + b'\n'
    if kind in b' -':
        if ro() != want: bad(f'旧の行が差分と違う: {body[:60]!r}')
    if kind in b' +':
        if rn() != want: bad(f'新の行が差分と違う: {body[:60]!r}')
    if kind == ord('-'): deleted += 1
    if kind == ord('+'): inserted += 1
    pending = None
for raw in diff:
    if raw.startswith(b'--- ') or raw.startswith(b'+++ '):
        if pending is None and hunks == 0: continue
    m = HUNK.match(raw)
    if m:
        flush(); hunks += 1
        os_ = int(m.group(1)); oc = int(m.group(2) or 1)
        start = os_ + 1 if oc == 0 else os_
        while oline + 1 < start:
            a, b = ro(), rn()
            if a != b: bad('まとまりの外の行が旧と新で違う')
        continue
    if raw.startswith(b'\\'):
        if pending: pending = (pending[0], pending[1], True)
        continue
    flush()
    pending = (raw[0], raw[1:].rstrip(b'\n'), False)
flush()
while True:
    a, b = ro(), rn()
    if a != b: bad('最後のまとまりの後の行が旧と新で違う')
    if not a: break
print(f'一致（まとまり {hunks}・削除 {deleted} 行・追加 {inserted} 行）')
