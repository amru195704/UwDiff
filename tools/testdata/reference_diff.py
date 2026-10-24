# > パス: tools/testdata/reference_diff.py
# 試験データの生成器が正しいかを、Python の csv で旧と新を照合して確かめる（小さいデータ用・全部メモリに載せる）。使い方: python3 reference_diff.py 旧.csv 新.csv 正解.csv 文字コード(utf-8|cp932)
import csv, sys
old, new, ans, enc = sys.argv[1:5]
def load(p):
    with open(p, encoding=enc, newline='') as f:
        r = csv.reader(f); h = next(r); return h, {row[0]: row for row in r}
h, O = load(old); _, N = load(new)
cols = {c: i for i, c in enumerate(h)}
got = set()
for k in O.keys() - N.keys(): got.add(('削除', k, '', '', ''))
for k in N.keys() - O.keys(): got.add(('追加', k, '', '', ''))
for k in O.keys() & N.keys():
    for i, (a, b) in enumerate(zip(O[k], N[k])):
        if a != b: got.add(('変更', k, h[i], a, b))
with open(ans, encoding='utf-8', newline='') as f:
    r = csv.reader(f); next(r); want = {tuple(x) for x in r}
print('rows old', len(O), 'new', len(N), 'answer', len(want), 'diff', len(got), 'MATCH' if got == want else 'MISMATCH')
if got != want: print(list(got - want)[:5], list(want - got)[:5]); sys.exit(1)
