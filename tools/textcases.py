# > パス: tools/textcases.py
# UwDiff: テキストの比較（行の並びを保つ・unified diff）と順番を問わない比較を、小さいデータで確かめる。
#   - 差分を patch で旧に当てると新とまったく同じになるか（正しさ）
#   - 消した行・足した行の数が diff（GNU／BSD）の 1.1 倍以内か（差分が小さいか）
#   - 空白・大文字小文字・改行コードを無視する指定、改行で終わらない最後の行、文字コードの違い、空のファイル、大きな挿入・入れ替え
#   - 順番を問わない比較（同じ行の数まで）
# 使い方: python3 -I tools/textcases.py <uvd の実行ファイル> <作業フォルダ> [行数=5000]
import json, os, random, shutil, subprocess, sys
from collections import Counter

UVD, WORK = sys.argv[1], sys.argv[2]
CHECK = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'testdata', 'check_unified.py')
HAS_PATCH, HAS_DIFF = shutil.which('patch') is not None, shutil.which('diff') is not None   # Windows には無いことが多い
N = int(sys.argv[3]) if len(sys.argv) > 3 else 5000
os.makedirs(WORK, exist_ok=True)
rnd = random.Random(20261024)
WORDS = ['杭', '基準点', 'ERROR', 'INFO', 'warn', '測点', 'ﾊﾝｶｸ', 'id=', '①', 'x', 'y', '"q"', ',', '\t']

def line(i):
    return f'{i:07d} ' + ' '.join(rnd.choice(WORDS) for _ in range(rnd.randint(0, 6)))

def make(n, dup_every=0):
    ls = [line(i) for i in range(n)]
    if dup_every:
        for i in range(0, n, dup_every): ls[i] = ''        # 空行や同じ行（目印にならない行）
    return ls

def mutate(ls, rate=0.02, blocks=3, big_insert=0, move=False):
    out = []
    i = 0
    while i < len(ls):
        x = rnd.random()
        if x < rate / 3: i += 1; continue                           # 削除
        if x < rate * 2 / 3: out.append(ls[i] + ' 改'); i += 1; continue   # 変更
        if x < rate: out.append(f'挿入 {rnd.random():.6f}')          # 追加
        out.append(ls[i]); i += 1
    for _ in range(blocks):                                          # 続いた塊の削除・追加
        if len(out) < 10: break
        p = rnd.randrange(len(out) - 5)
        if rnd.random() < 0.5: del out[p:p + rnd.randint(5, 60)]
        else: out[p:p] = [f'塊 {k} {rnd.random():.4f}' for k in range(rnd.randint(5, 60))]
    if big_insert:
        p = len(out) // 2
        out[p:p] = [f'大きな挿入 {k}' for k in range(big_insert)]
    if move and len(out) > 100:
        a = rnd.randrange(len(out) // 2); seg = out[a:a + 40]; del out[a:a + 40]; out[len(out) - 10:len(out) - 10] = seg
    return out

def write(path, ls, enc='utf-8', nl='\n', final=True):
    text = nl.join(ls) + (nl if final and ls else '')
    with open(path, 'wb') as f: f.write(text.encode(enc))

def count_lines(diff_path):
    d = a = 0
    with open(diff_path, 'rb') as f:
        for l in f:
            if l.startswith(b'---') or l.startswith(b'+++'): continue
            if l.startswith(b'-'): d += 1
            elif l.startswith(b'+'): a += 1
    return d, a

def gnu_count(o, n, extra=()):
    p = subprocess.run(['diff', *extra, o, n], capture_output=True)
    d = sum(1 for l in p.stdout.splitlines() if l.startswith(b'< '))
    a = sum(1 for l in p.stdout.splitlines() if l.startswith(b'> '))
    return d, a

results = []
def case(name, old, new, enc='utf-8', nenc=None, args=(), nl='\n', nnl=None, final=True, nfinal=None, expect_same=None, patch=True, minimal=True):
    o, n = os.path.join(WORK, name + '_old.txt'), os.path.join(WORK, name + '_new.txt')
    write(o, old, enc, nl, final)
    write(n, new, nenc or enc, nnl or nl, final if nfinal is None else nfinal)
    d = os.path.join(WORK, name + '.diff')
    js = os.path.join(WORK, name + '.json')
    p = subprocess.run([UVD, o, n, '-o', d, '-json', js, '-q', *args], capture_output=True, text=True)
    ok = p.returncode in (0, 1)
    msg = []
    if not ok: msg.append(f'終了コード {p.returncode} {p.stderr.strip()}')
    j = json.load(open(js, encoding='utf-8')) if ok else {}
    if ok and expect_same is not None:
        same = j['deleted'] == 0 and j['inserted'] == 0
        if same != expect_same: ok = False; msg.append(f'同じ={same}（期待 {expect_same}）')
        if (p.returncode == 0) != same: ok = False; msg.append('終了コードと結果が合わない')
    if ok and patch and not HAS_PATCH:
        # patch が無い（Windows など）: 差分が「旧 → 新」を正しく表すかを check_unified.py で確かめる
        pc = subprocess.run([sys.executable, '-I', CHECK, o, n, d], capture_output=True, text=True)
        if pc.returncode != 0: ok = False; msg.append('差分が旧 → 新を表していない ' + pc.stdout.strip())
        else: msg.append('check_unified で一致')
    elif ok and patch:
        t = os.path.join(WORK, name + '_patched.txt')
        with open(o, 'rb') as f, open(t, 'wb') as g: g.write(f.read())
        pp = subprocess.run(['patch', '-s', '-f', t, d], capture_output=True, text=True)
        if pp.returncode != 0 or open(t, 'rb').read() != open(n, 'rb').read():
            ok = False; msg.append(f'patch で新にならない {pp.stdout.strip()} {pp.stderr.strip()}')
        else: msg.append('patch で新に一致')
    if ok and minimal and HAS_DIFF:
        ud, ua = count_lines(d); gd, ga = gnu_count(o, n)
        msg.append(f'行 -{ud}/+{ua}（diff -{gd}/+{ga}）')
        if ud + ua > (gd + ga) * 1.1 + 2: ok = False; msg.append('差分が diff より大きすぎる')
    print(f'{"○" if ok else "×"} {name}: ' + '・'.join(msg))
    results.append(ok)

base = make(N)
case('small_edits', base, mutate(base))
case('many_edits', base, mutate(base, rate=0.2, blocks=10))
case('dup_and_blank', make(N, 7), mutate(make(N, 7)))
case('big_insert', base, mutate(base, big_insert=N * 2))
case('moved_block', base, mutate(base, move=True), minimal=False)
case('identical', base, list(base), expect_same=True)
case('empty_old', [], base[:50])
case('empty_new', base[:50], [])
case('both_empty', [], [], expect_same=True)
case('no_final_newline_new', base[:200], base[:200], nfinal=False, expect_same=False)
case('no_final_newline_both', base[:200], mutate(base[:200]), final=False)
case('crlf_vs_lf_ignored', base, list(base), nnl='\r\n', expect_same=True, patch=False, minimal=False)
case('crlf_vs_lf_strict', base[:300], list(base[:300]), nnl='\r\n', args=['-strict-eol'], expect_same=False)
case('crlf_both', base, mutate(base), nl='\r\n')
case('cp932_both', base, mutate(base), enc='cp932')
case('eucjp_both', [l.replace('①', '') for l in base], [l.replace('①', '') for l in mutate(base)], enc='euc_jp')
case('utf16_both', base[:1000], mutate(base[:1000]), enc='utf-16', patch=False, minimal=False)   # diff は UTF-16 をバイナリとみなすので比べない
case('mixed_cp932_utf8_same_text', base, list(base), enc='cp932', nenc='utf-8', expect_same=True, patch=False, minimal=False)
sp = [l.replace(' ', '  ') + '   ' for l in base[:500]]
case('ignore_space_change', base[:500], sp, args=['-b'], expect_same=True, patch=False, minimal=False)
case('ignore_all_space', base[:500], [l.replace(' ', '') for l in base[:500]], args=['-w'], expect_same=True, patch=False, minimal=False)
case('ignore_case', base[:500], [l.upper() for l in base[:500]], args=['-i'], expect_same=True, patch=False, minimal=False)
case('context_0', base, mutate(base), args=['-U', '0'])
case('context_10', base, mutate(base), args=['-U', '10'])

# 順番を問わない比較: 同じ行の数まで（空の行は数えない）
old_u = base + base[:30] + ['']
new_u = list(base); rnd.shuffle(new_u); new_u = new_u[:-40] + ['新しい行 1', '新しい行 2', base[5]]
o, n = os.path.join(WORK, 'unordered_old.txt'), os.path.join(WORK, 'unordered_new.txt')
write(o, old_u); write(n, new_u)
out, js = os.path.join(WORK, 'unordered.csv'), os.path.join(WORK, 'unordered.json')
p = subprocess.run([UVD, o, n, '-unordered', '-o', out, '-json', js, '-q'], capture_output=True, text=True)
co, cn = Counter(l for l in old_u if l), Counter(l for l in new_u if l)
want_del, want_add = sum((co - cn).values()), sum((cn - co).values())
j = json.load(open(js, encoding='utf-8')) if p.returncode in (0, 1) else {'added': -1, 'deleted': -1}
import csv
with open(out, encoding='utf-8', newline='') as f:
    rows = list(csv.reader(f))[1:]
got = Counter((r[0], r[1]) for r in rows)
want = Counter({('削除', k): v for k, v in (co - cn).items()}) + Counter({('追加', k): v for k, v in (cn - co).items()})
ok = j['added'] == want_add and j['deleted'] == want_del and got == want and p.returncode == 1
print(f'{"○" if ok else "×"} unordered: 追加 {j["added"]}/{want_add}・削除 {j["deleted"]}/{want_del}・中身 {"一致" if got == want else "不一致"}')
results.append(ok)

print('すべて一致' if all(results) else '不一致あり')
sys.exit(0 if all(results) else 1)
